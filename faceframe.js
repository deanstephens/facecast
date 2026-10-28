// Face zoom: finds your face in your own camera picture and works out the crop of it that fills an avatar's face
// screen with your face, moving smoothly as you move. The picture itself is sent untouched; the crop (four numbers)
// travels with your pose, and every face screen showing you (in the game and in your self view) shows just that
// part of the picture. So the video never stops, even while this tab is in the background and can't draw.
//
// The face detector is MediaPipe's BlazeFace short-range model (WebAssembly, vendored in lib/mediapipe by
// Tools/web/vendor.mjs), loaded the first time it's needed. Without it, or without a face in view, the crop is the
// whole picture (cut to the screen's shape). Adapted from fun-meets' faceframe.js, which crops a canvas instead.

export const SCREEN_ASPECT = 4 / 3   // an avatar's face screen (Blender/MODELS.md)
const DETECT_EVERY = 150             // ms between looks for the face
const HOLD = 1500                    // ms to stay on a face that has slipped out of detection before zooming out
const EASE = 4                       // how fast the crop follows (per second)

// The crop {x, y, w, h} (fractions of a vw × vh picture, from its top left) of the screen's shape that frames `face`
// (a box in pixels): the face takes `faceFrac` of the crop's height, a little below the middle so the hair fits, and
// the crop stays inside the picture and zooms at most 4×. With no face, the largest crop that fits, centred. Pure,
// so the tests can check it (Tools/web/faceframe.test.mjs).
export function computeCrop(face, vw, vh, aspect = SCREEN_ASPECT, faceFrac = 0.58) {
  let maxW = vw
  let maxH = vw / aspect
  if (maxH > vh) {
    maxH = vh
    maxW = vh * aspect
  }
  if (!face) return {x: (vw - maxW) / 2 / vw, y: (vh - maxH) / 2 / vh, w: maxW / vw, h: maxH / vh}
  const h = Math.min(maxH, Math.max(maxH / 4, face.h / faceFrac, face.w / faceFrac / aspect))
  const w = h * aspect
  const cx = face.x + face.w / 2
  const cy = face.y + face.h / 2 - h * 0.06
  const x = Math.min(Math.max(cx - w / 2, 0), vw - w)
  const y = Math.min(Math.max(cy - h / 2, 0), vh - h)
  return {x: x / vw, y: y / vh, w: w / vw, h: h / vh}
}

// From crop a towards crop b by t (0..1).
export function blend(a, b, t) {
  return {x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, w: a.w + (b.w - a.w) * t, h: a.h + (b.h - a.h) * t}
}

// A crop from the network: four numbers, each a sensible fraction of the picture; null if not.
export function cleanCrop(c) {
  if (!c || c.length < 4) return null
  const [x, y, w, h] = Array.from(c, Number)
  if (![x, y, w, h].every(Number.isFinite) || w < 0.05 || h < 0.05 || w > 1.001 || h > 1.001) return null
  if (x < -0.001 || y < -0.001 || x + w > 1.002 || y + h > 1.002) return null
  return {x, y, w, h}
}

async function loadDetector(base) {
  const {FaceDetector} = await import(base + 'vision_bundle.mjs')
  const fileset = {wasmLoaderPath: base + 'vision_wasm_internal.js', wasmBinaryPath: base + 'vision_wasm_internal.wasm'}
  let last
  for (const delegate of ['GPU', 'CPU']) {
    try {
      return await FaceDetector.createFromOptions(fileset, {
        baseOptions: {modelAssetPath: base + 'blaze_face_short_range.tflite', delegate},
        runningMode: 'VIDEO',
        minDetectionConfidence: 0.5,
      })
    } catch (e) {
      last = e
    }
  }
  throw last
}

// Follows the face in `video` (your camera). Call tick() every animation frame; `crop` is where to look.
export class FaceZoom {
  constructor(video, {enabled = true, base = new URL('lib/mediapipe/', document.baseURI).href} = {}) {
    this.video = video
    this.enabled = enabled
    this.base = base
    this.crop = null        // what to show now (smoothed)
    this.target = null      // where it's heading
    this.state = 'idle'     // idle → loading → ready, or failed
    this.faces = 0          // faces in the last look
    this.detector = null
    this.lastLook = 0
    this.lastSeen = -Infinity
    this.lastTick = 0
  }

  setEnabled(on) {
    this.enabled = !!on
  }

  tick(now) {
    const video = this.video
    const vw = video.videoWidth
    const vh = video.videoHeight
    if (!vw || !vh) return
    if (this.enabled && this.state === 'idle') this.load()
    if (this.enabled && this.detector && now - this.lastLook >= DETECT_EVERY && video.readyState >= 2) {
      this.lastLook = now
      this.look(now, vw, vh)  // aims the target at a face it finds
    }
    // Zoom off, no detector (yet), or no face for a while: the whole picture.
    if (!this.enabled || !this.detector || now - this.lastSeen > HOLD) this.target = computeCrop(null, vw, vh)

    const dt = Math.min(0.25, Math.max(0, (now - (this.lastTick || now)) / 1000))
    this.lastTick = now
    this.crop = this.crop ? blend(this.crop, this.target, 1 - Math.exp(-EASE * dt)) : {...this.target}
  }

  look(now, vw, vh) {
    let result
    try {
      result = this.detector.detectForVideo(this.video, now)
    } catch {
      return
    }
    const boxes = (result?.detections || []).map(d => d.boundingBox).filter(Boolean)
    this.faces = boxes.length
    if (!boxes.length) return
    // The biggest face: whoever is nearest the camera.
    const b = boxes.reduce((a, c) => (c.width * c.height > a.width * a.height ? c : a))
    this.target = computeCrop({x: b.originX, y: b.originY, w: b.width, h: b.height}, vw, vh)
    this.lastSeen = now
  }

  load() {
    this.state = 'loading'
    loadDetector(this.base).then(d => {
      this.detector = d
      this.state = 'ready'
    }).catch(e => {
      this.state = 'failed'
      console.warn('[facecast] face zoom unavailable, showing the whole picture:', e?.message || e)
    })
  }
}
