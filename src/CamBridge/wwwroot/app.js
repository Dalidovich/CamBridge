(() => {
  "use strict";

  const PING_INTERVAL_MS = 3000;
  const MAX_BUFFERED_BYTES = 2 * 1024 * 1024;
  const LOCK_JPEG_QUALITY = 0.92;
  const RECORD_MIME_CANDIDATES = ["video/mp4", "video/webm;codecs=vp9", "video/webm;codecs=vp8", "video/webm"];

  const elements = {
    status: document.getElementById("status"),
    message: document.getElementById("message"),
    hint: document.getElementById("hint"),
    video: document.getElementById("preview"),
    startButton: document.getElementById("start-button"),
    disconnectButton: document.getElementById("disconnect-button"),
    lockButton: document.getElementById("lock-button"),
    lockedFrame: document.getElementById("locked-frame"),
    lockedIndicator: document.getElementById("locked-indicator"),
    torchIndicator: document.getElementById("torch-indicator"),
    recordingIndicator: document.getElementById("recording-indicator"),
    streamingIndicator: document.getElementById("streaming-indicator")
  };

  const token = new URLSearchParams(window.location.search).get("t");
  const textEncoder = new TextEncoder();

  const state = {
    mediaStream: null,
    track: null,
    socket: null,
    pingTimer: null,
    wakeLock: null,
    capabilities: null,
    streamTimer: null,
    streamBusy: false,
    recording: false,
    lockedFrameUrl: null,
    locking: false,
    connected: false,
    endedByUser: false
  };

  function setStatus(text, kind) {
    elements.status.textContent = text;
    elements.status.className = "status status-" + kind;
  }

  function showMessage(text) {
    elements.message.textContent = text;
  }

  function setVisible(element, visible) {
    element.hidden = !visible;
  }

  function describeCameraError(error) {
    switch (error && error.name) {
      case "NotAllowedError":
      case "SecurityError":
        return "Camera access was denied. Allow camera access for this site in the browser settings and reload the page.";
      case "NotFoundError":
      case "OverconstrainedError":
        return "No suitable camera was found on this device.";
      case "NotReadableError":
      case "AbortError":
        return "The camera is busy or unavailable. Close other apps that use the camera and try again.";
      default:
        return "Could not start the camera: " + (error && error.message ? error.message : String(error));
    }
  }

  function readCapabilities(track) {
    let raw = {};
    try {
      raw = typeof track.getCapabilities === "function" ? track.getCapabilities() || {} : {};
    } catch (error) {
      raw = {};
    }

    const settings = typeof track.getSettings === "function" ? track.getSettings() || {} : {};
    const zoom = raw.zoom && typeof raw.zoom.min === "number" && typeof raw.zoom.max === "number"
      ? { min: raw.zoom.min, max: raw.zoom.max, step: typeof raw.zoom.step === "number" ? raw.zoom.step : 0.1 }
      : null;
    const recordMimeTypes = typeof window.MediaRecorder === "function" && typeof MediaRecorder.isTypeSupported === "function"
      ? RECORD_MIME_CANDIDATES.filter((type) => MediaRecorder.isTypeSupported(type))
      : [];

    return {
      torch: raw.torch === true || (Array.isArray(raw.torch) && raw.torch.includes(true)),
      zoom: zoom,
      width: settings.width || elements.video.videoWidth || 0,
      height: settings.height || elements.video.videoHeight || 0,
      recordMimeTypes: recordMimeTypes
    };
  }

  async function startCamera() {
    if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
      setStatus("Error", "error");
      showMessage("This browser cannot access the camera on this page. Open the link in Safari or Chrome over HTTPS.");
      return;
    }

    setVisible(elements.startButton, false);
    setStatus("Starting camera", "connecting");
    showMessage("Waiting for camera permission...");

    try {
      state.mediaStream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: "environment" }, width: { ideal: 1920 }, height: { ideal: 1080 } },
        audio: false
      });
    } catch (error) {
      setStatus("Camera error", "error");
      showMessage(describeCameraError(error));
      setVisible(elements.startButton, true);
      return;
    }

    state.track = state.mediaStream.getVideoTracks()[0];
    elements.video.srcObject = state.mediaStream;
    try {
      await elements.video.play();
    } catch (error) {
      showMessage("Tap the video to start the preview.");
    }

    await waitForVideoDimensions();
    state.capabilities = readCapabilities(state.track);
    connect();
  }

  function waitForVideoDimensions() {
    if (elements.video.videoWidth > 0) {
      return Promise.resolve();
    }

    return new Promise((resolve) => {
      const done = () => resolve();
      elements.video.addEventListener("loadedmetadata", done, { once: true });
      setTimeout(done, 3000);
    });
  }

  async function explainRejection() {
    try {
      const response = await fetch("/ws/status?t=" + encodeURIComponent(token), { cache: "no-store" });
      if (response.status === 409) {
        return { status: "Busy", kind: "warning", message: "Another phone is already connected to this PC. Disconnect it first." };
      }

      if (response.status === 403) {
        return { status: "Invalid link", kind: "error", message: "This pairing link is invalid or expired. Scan the QR code currently shown on the PC." };
      }

      if (response.ok) {
        return null;
      }
    } catch (error) {
      return { status: "Offline", kind: "error", message: "Cannot reach the PC. Make sure the phone and the PC are on the same network." };
    }

    return { status: "Error", kind: "error", message: "The PC rejected the connection." };
  }

  async function connect() {
    setStatus("Connecting", "connecting");
    showMessage("Connecting to the PC...");

    const rejection = await explainRejection();
    if (rejection) {
      showRejection(rejection);
      return;
    }

    const socket = new WebSocket("wss://" + window.location.host + "/ws?t=" + encodeURIComponent(token));
    socket.binaryType = "arraybuffer";
    state.socket = socket;

    socket.addEventListener("open", onSocketOpen);
    socket.addEventListener("message", onSocketMessage);
    socket.addEventListener("close", onSocketClose);
  }

  function showRejection(rejection) {
    setStatus(rejection.status, rejection.kind);
    showMessage(rejection.message);
    stopCamera();
  }

  function onSocketOpen() {
    state.connected = true;
    setStatus("Connected", "connected");
    showMessage("Connected. The PC can now use this camera.");
    setVisible(elements.disconnectButton, true);
    setVisible(elements.lockButton, true);
    setVisible(elements.hint, true);

    sendJson({ type: "hello", userAgent: navigator.userAgent, capabilities: state.capabilities });
    state.pingTimer = setInterval(() => sendJson({ type: "ping" }), PING_INTERVAL_MS);
    requestWakeLock();
  }

  async function onSocketClose() {
    const wasConnected = state.connected;
    cleanupConnection();

    if (state.endedByUser || wasConnected) {
      setStatus("Disconnected", "warning");
      showMessage("Disconnected — scan the new QR on the PC.");
      return;
    }

    const rejection = await explainRejection();
    showRejection(rejection || { status: "Disconnected", kind: "warning", message: "Disconnected — scan the new QR on the PC." });
  }

  function cleanupConnection() {
    state.connected = false;
    clearInterval(state.pingTimer);
    state.pingTimer = null;
    stopStreaming();
    clearLockedFrame();
    releaseWakeLock();
    stopCamera();
    state.socket = null;
    setVisible(elements.disconnectButton, false);
    setVisible(elements.lockButton, false);
    setVisible(elements.hint, false);
    setVisible(elements.torchIndicator, false);
  }

  function stopCamera() {
    if (state.mediaStream) {
      state.mediaStream.getTracks().forEach((track) => track.stop());
    }

    state.mediaStream = null;
    state.track = null;
    elements.video.srcObject = null;
  }

  function disconnect() {
    state.endedByUser = true;
    if (state.socket) {
      state.socket.close(1000, "Disconnected on the phone");
    }
  }

  function sendJson(message) {
    if (state.socket && state.socket.readyState === WebSocket.OPEN) {
      state.socket.send(JSON.stringify(message));
    }
  }

  function sendBinary(header, blob) {
    if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
      return;
    }

    const headerBytes = textEncoder.encode(JSON.stringify(header));
    const lengthPrefix = new Uint8Array(4);
    new DataView(lengthPrefix.buffer).setUint32(0, headerBytes.length, false);
    state.socket.send(new Blob([lengthPrefix, headerBytes, blob]));
  }

  function sendResult(id, ok, error) {
    const message = { type: "result", id: id, ok: ok };
    if (!ok) {
      message.error = error || "Unknown error";
    }

    sendJson(message);
  }

  function onSocketMessage(event) {
    if (typeof event.data !== "string") {
      return;
    }

    let message;
    try {
      message = JSON.parse(event.data);
    } catch (error) {
      return;
    }

    switch (message.type) {
      case "snapshot":
        handleSnapshot(message);
        break;
      case "stream":
        handleStream(message);
        break;
      case "torch":
        handleTorch(message);
        break;
      case "zoom":
        handleZoom(message);
        break;
      case "record":
        handleRecord(message);
        break;
      default:
        break;
    }
  }

  function captureFrame(quality, maxWidth) {
    const video = elements.video;
    if (!video.videoWidth || !video.videoHeight) {
      return Promise.reject(new Error("The camera preview is not ready."));
    }

    const scale = Math.min(1, (maxWidth || video.videoWidth) / video.videoWidth);
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
    canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
    canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);

    return new Promise((resolve, reject) => {
      canvas.toBlob((blob) => {
        if (blob) {
          resolve(blob);
        } else {
          reject(new Error("Could not encode the frame."));
        }
      }, "image/jpeg", quality);
    });
  }

  async function handleSnapshot(message) {
    try {
      const blob = await captureFrame(message.quality, message.maxWidth);
      sendBinary({ id: message.id, mime: "image/jpeg", ok: true }, blob);
    } catch (error) {
      sendResult(message.id, false, error.message);
    }
  }

  function handleStream(message) {
    stopStreaming();
    if (!message.enabled) {
      return;
    }

    const interval = Math.max(33, Math.round(1000 / Math.max(1, message.fps || 10)));
    setVisible(elements.streamingIndicator, true);
    state.streamTimer = setInterval(async () => {
      if (state.streamBusy || state.lockedFrameUrl || !state.socket || state.socket.bufferedAmount > MAX_BUFFERED_BYTES) {
        return;
      }

      state.streamBusy = true;
      try {
        const blob = await captureFrame(message.quality, message.maxWidth);
        sendBinary({ id: "stream", mime: "image/jpeg", ok: true }, blob);
      } catch (error) {
        return;
      } finally {
        state.streamBusy = false;
      }
    }, interval);
  }

  function stopStreaming() {
    clearInterval(state.streamTimer);
    state.streamTimer = null;
    state.streamBusy = false;
    setVisible(elements.streamingIndicator, false);
  }

  async function toggleLock() {
    if (state.lockedFrameUrl) {
      sendJson({ type: "unlock" });
      clearLockedFrame();
      showMessage("Frame unlocked. The PC can use the live camera again.");
      return;
    }

    if (state.locking || !state.connected) {
      return;
    }

    state.locking = true;
    elements.lockButton.disabled = true;
    try {
      const blob = await captureFrame(LOCK_JPEG_QUALITY, null);
      if (!state.connected) {
        return;
      }

      sendBinary({ id: "lock", mime: "image/jpeg", ok: true }, blob);
      showLockedFrame(blob);
      showMessage("Frame locked. The PC can only get this frame until you unlock it.");
    } catch (error) {
      showMessage("Could not lock the frame: " + (error.message || String(error)));
    } finally {
      state.locking = false;
      elements.lockButton.disabled = false;
    }
  }

  function showLockedFrame(blob) {
    state.lockedFrameUrl = URL.createObjectURL(blob);
    elements.lockedFrame.src = state.lockedFrameUrl;
    setVisible(elements.lockedFrame, true);
    setVisible(elements.lockedIndicator, true);
    elements.lockButton.textContent = "Unlock";
    elements.lockButton.classList.add("is-locked");
  }

  function clearLockedFrame() {
    if (state.lockedFrameUrl) {
      URL.revokeObjectURL(state.lockedFrameUrl);
    }

    state.lockedFrameUrl = null;
    elements.lockedFrame.removeAttribute("src");
    setVisible(elements.lockedFrame, false);
    setVisible(elements.lockedIndicator, false);
    elements.lockButton.textContent = "Lock frame";
    elements.lockButton.classList.remove("is-locked");
  }

  async function applyAdvancedConstraint(constraint) {
    if (!state.track || typeof state.track.applyConstraints !== "function") {
      throw new Error("The camera is not available.");
    }

    await state.track.applyConstraints({ advanced: [constraint] });
  }

  async function handleTorch(message) {
    if (!state.capabilities || !state.capabilities.torch) {
      sendResult(message.id, false, "Torch is not supported on this device.");
      return;
    }

    try {
      await applyAdvancedConstraint({ torch: message.on === true });
      setVisible(elements.torchIndicator, message.on === true);
      sendResult(message.id, true);
    } catch (error) {
      sendResult(message.id, false, error.message || String(error));
    }
  }

  async function handleZoom(message) {
    const zoom = state.capabilities && state.capabilities.zoom;
    if (!zoom) {
      sendResult(message.id, false, "Zoom is not supported on this device.");
      return;
    }

    const value = Math.min(zoom.max, Math.max(zoom.min, Number(message.value)));
    try {
      await applyAdvancedConstraint({ zoom: value });
      sendResult(message.id, true);
    } catch (error) {
      sendResult(message.id, false, error.message || String(error));
    }
  }

  function pickRecorderMimeType() {
    if (typeof window.MediaRecorder !== "function") {
      return null;
    }

    if (typeof MediaRecorder.isTypeSupported !== "function") {
      return "";
    }

    return RECORD_MIME_CANDIDATES.find((type) => MediaRecorder.isTypeSupported(type)) ?? null;
  }

  function handleRecord(message) {
    if (state.recording) {
      sendResult(message.id, false, "A recording is already in progress.");
      return;
    }

    const mimeType = pickRecorderMimeType();
    if (mimeType === null || !state.mediaStream) {
      sendResult(message.id, false, "Video recording is not supported on this device.");
      return;
    }

    let recorder;
    try {
      recorder = mimeType ? new MediaRecorder(state.mediaStream, { mimeType: mimeType }) : new MediaRecorder(state.mediaStream);
    } catch (error) {
      sendResult(message.id, false, error.message || String(error));
      return;
    }

    const chunks = [];
    const seconds = Math.min(30, Math.max(1, Number(message.seconds) || 1));
    state.recording = true;
    setVisible(elements.recordingIndicator, true);

    recorder.addEventListener("dataavailable", (event) => {
      if (event.data && event.data.size > 0) {
        chunks.push(event.data);
      }
    });

    recorder.addEventListener("stop", () => {
      state.recording = false;
      setVisible(elements.recordingIndicator, false);
      const type = (recorder.mimeType || mimeType || "video/webm").split(";")[0];
      if (chunks.length === 0) {
        sendResult(message.id, false, "The recording produced no data.");
        return;
      }

      sendBinary({ id: message.id, mime: type, ok: true }, new Blob(chunks, { type: type }));
    });

    recorder.addEventListener("error", (event) => {
      state.recording = false;
      setVisible(elements.recordingIndicator, false);
      sendResult(message.id, false, event.error ? event.error.message : "Recording failed.");
    });

    recorder.start(1000);
    setTimeout(() => {
      if (recorder.state !== "inactive") {
        recorder.stop();
      }
    }, seconds * 1000);
  }

  async function requestWakeLock() {
    if (!("wakeLock" in navigator) || !state.connected) {
      return;
    }

    try {
      state.wakeLock = await navigator.wakeLock.request("screen");
      state.wakeLock.addEventListener("release", () => {
        state.wakeLock = null;
      });
    } catch (error) {
      state.wakeLock = null;
    }
  }

  function releaseWakeLock() {
    if (state.wakeLock) {
      state.wakeLock.release().catch(() => undefined);
      state.wakeLock = null;
    }
  }

  function onVisibilityChange() {
    if (document.visibilityState === "visible" && state.connected && !state.wakeLock) {
      requestWakeLock();
    }
  }

  function initialize() {
    elements.startButton.addEventListener("click", startCamera);
    elements.disconnectButton.addEventListener("click", disconnect);
    elements.lockButton.addEventListener("click", toggleLock);
    elements.video.addEventListener("click", () => elements.video.play().catch(() => undefined));
    document.addEventListener("visibilitychange", onVisibilityChange);

    if (!token) {
      setStatus("Not paired", "warning");
      showMessage("Scan the QR code shown on the PC.");
      return;
    }

    setStatus("Ready", "idle");
    showMessage("Tap \"Start camera\" to share this camera with the PC.");
    setVisible(elements.startButton, true);
  }

  initialize();
})();
