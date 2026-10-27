// The game's side of the bridge to the page (WebGLTemplates/Facecast/facecast.js, which defines window.facecast).
// C# calls these through WebBridge.cs. The page owns the WebRTC room, the camera, the microphone, the voice mix and
// the note you write on.
//
// Video: each face screen samples a WebGL texture created here and wrapped in C# with
// Texture2D.CreateExternalTexture. Every frame, FC_UpdateVideoTexture copies the newest frame of a peer's <video>
// element into it on the GPU (texImage2D from the element), so pictures never pass through C# or the CPU. The
// texture is sRGB, like Unity's own colour textures in linear colour space, and flipped on upload so v runs up.
mergeInto(LibraryManager.library, {
  FC_Ready: function () {
    if (window.facecast) window.facecast.unityReady();
  },

  FC_SendPose: function (x, y, z, yaw, pitch, time) {
    if (window.facecast) window.facecast.sendPose(x, y, z, yaw, pitch, time);
  },

  FC_ToggleMic: function () {
    if (window.facecast) window.facecast.toggleMic();
  },

  FC_ToggleCam: function () {
    if (window.facecast) window.facecast.toggleCam();
  },

  // The browser has the last word on pointer lock (Esc releases it without telling Unity's Cursor.lockState).
  FC_PointerLocked: function () {
    return document.pointerLockElement ? 1 : 0;
  },

  FC_ToggleShare: function () {
    if (window.facecast) window.facecast.toggleShare();
  },

  FC_ShareAt: function (x, y, z, yaw) {
    if (window.facecast) window.facecast.shareAt(x, y, z, yaw);
  },

  FC_ToggleZoom: function () {
    if (window.facecast) window.facecast.toggleZoom();
  },

  // Writes the part of key's picture their face is in (x, y, w, h from the top left, fractions) into the float[4]
  // at ptr. Returns 0, writing nothing, when the whole picture should show.
  FC_VideoCrop: function (key, ptr) {
    var c = window.facecast ? window.facecast.videoCrop(UTF8ToString(key)) : null;
    if (!c) return 0;
    var i = ptr >> 2;
    HEAPF32[i] = c[0];
    HEAPF32[i + 1] = c[1];
    HEAPF32[i + 2] = c[2];
    HEAPF32[i + 3] = c[3];
    return 1;
  },

  FC_AudioLevel: function (key) {
    return window.facecast ? window.facecast.audioLevel(UTF8ToString(key)) : 0;
  },

  FC_CreateVideoTexture: function () {
    var gl = GLctx;
    var texture = gl.createTexture();
    var id = GL.getNewId(GL.textures);
    texture.name = id;
    GL.textures[id] = texture;
    var previous = gl.getParameter(gl.TEXTURE_BINDING_2D);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.SRGB8_ALPHA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([0, 0, 0, 255]));
    gl.bindTexture(gl.TEXTURE_2D, previous);
    return id;
  },

  // Returns width << 16 | height of the picture now in the texture, or 0 when there's no video to show.
  FC_UpdateVideoTexture: function (key, id) {
    var fc = window.facecast;
    var texture = GL.textures[id];
    if (!fc || !texture) return 0;
    var video = fc.videoFor(UTF8ToString(key));
    if (!video || video.readyState < 2 || !video.videoWidth || !video.videoHeight) return 0;
    var size = (video.videoWidth << 16) | video.videoHeight;
    var stamp = fc.frameStamp(video);
    if (texture.fcVideo === video && texture.fcStamp === stamp) return size;  // nothing new since the last copy

    var gl = GLctx;
    // Unity keeps its own record of this state, so put back everything that's touched.
    var previous = gl.getParameter(gl.TEXTURE_BINDING_2D);
    var unpackBuffer = gl.getParameter(gl.PIXEL_UNPACK_BUFFER_BINDING);
    var rowLength = gl.getParameter(gl.UNPACK_ROW_LENGTH);
    var skipRows = gl.getParameter(gl.UNPACK_SKIP_ROWS);
    var skipPixels = gl.getParameter(gl.UNPACK_SKIP_PIXELS);
    var alignment = gl.getParameter(gl.UNPACK_ALIGNMENT);
    var flip = gl.getParameter(gl.UNPACK_FLIP_Y_WEBGL);
    if (unpackBuffer) gl.bindBuffer(gl.PIXEL_UNPACK_BUFFER, null);
    if (rowLength) gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0);
    if (skipRows) gl.pixelStorei(gl.UNPACK_SKIP_ROWS, 0);
    if (skipPixels) gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, 0);
    if (alignment !== 4) gl.pixelStorei(gl.UNPACK_ALIGNMENT, 4);
    if (!flip) gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    var ok = true;
    try {
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.SRGB8_ALPHA8, gl.RGBA, gl.UNSIGNED_BYTE, video);
    } catch (e) {
      ok = false;  // the element can throw while its stream is being swapped
    }
    if (!flip) gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.bindTexture(gl.TEXTURE_2D, previous);
    if (alignment !== 4) gl.pixelStorei(gl.UNPACK_ALIGNMENT, alignment);
    if (skipPixels) gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, skipPixels);
    if (skipRows) gl.pixelStorei(gl.UNPACK_SKIP_ROWS, skipRows);
    if (rowLength) gl.pixelStorei(gl.UNPACK_ROW_LENGTH, rowLength);
    if (unpackBuffer) gl.bindBuffer(gl.PIXEL_UNPACK_BUFFER, unpackBuffer);
    if (!ok) return texture.fcVideo === video ? size : 0;
    texture.fcVideo = video;
    texture.fcStamp = stamp;
    fc.countUpload(UTF8ToString(key));
    return size;
  },

  // Sticky notes and question cards: the game owns the boards, the box and the table; the page carries "notes"
  // messages and shows the note or card you write on.
  FC_SendNotes: function (json, target) {
    if (window.facecast) window.facecast.sendNotes(UTF8ToString(json), UTF8ToString(target));
  },

  FC_OpenNoteEditor: function (text, color, question) {
    if (window.facecast) window.facecast.openNoteEditor(UTF8ToString(text), color, !!question);
  },

  FC_CloseNoteEditor: function () {
    if (window.facecast) window.facecast.closeNoteEditor();
  },

  FC_ReportNotes: function (json) {
    if (window.facecast) window.facecast.reportNotes(UTF8ToString(json));
  },

  FC_DeleteVideoTexture: function (id) {
    var texture = GL.textures[id];
    if (!texture) return;
    GLctx.deleteTexture(texture);
    GL.textures[id] = null;
  },
});
