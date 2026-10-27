// Unit tests for the face zoom's arithmetic (Assets/WebGLTemplates/Facecast/faceframe.js):  cd Tools/web && npm test
import test from 'node:test'
import assert from 'node:assert/strict'
import {computeCrop, blend, cleanCrop, SCREEN_ASPECT} from '../../Assets/WebGLTemplates/Facecast/faceframe.js'

const close = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) <= eps, `${a} ≠ ${b}`)
const aspectInPixels = (c, vw, vh) => (c.w * vw) / (c.h * vh)
const inside = c => assert.ok(c.x >= -1e-9 && c.y >= -1e-9 && c.x + c.w <= 1 + 1e-9 && c.y + c.h <= 1 + 1e-9, JSON.stringify(c))

test('without a face, the whole picture, cut to the screen shape', () => {
  assert.deepEqual(computeCrop(null, 640, 480), {x: 0, y: 0, w: 1, h: 1})
  const wide = computeCrop(null, 1280, 720)
  close(aspectInPixels(wide, 1280, 720), SCREEN_ASPECT)
  close(wide.h, 1)
  close(wide.x, (1 - wide.w) / 2)
})

test('a face fills the same share of the crop wherever it is', () => {
  const face = {x: 200, y: 160, w: 80, h: 100}
  const c = computeCrop(face, 640, 480)
  close(aspectInPixels(c, 640, 480), SCREEN_ASPECT)
  close(100 / (c.h * 480), 0.58)
  const cx = (c.x + c.w / 2) * 640
  close(cx, 240, 1e-6)  // centred across the face
  assert.ok((c.y + c.h / 2) * 480 < 210, 'a little above the face centre, so the hair fits')
  inside(c)
})

test('the crop stays inside the picture', () => {
  for (const face of [{x: 0, y: 0, w: 90, h: 110}, {x: 560, y: 380, w: 80, h: 100}, {x: 600, y: 0, w: 40, h: 40}]) {
    const c = computeCrop(face, 640, 480)
    inside(c)
    close(aspectInPixels(c, 640, 480), SCREEN_ASPECT)
  }
})

test('zoom is limited both ways', () => {
  const far = computeCrop({x: 300, y: 200, w: 8, h: 10}, 640, 480)
  close(far.h, 0.25)                         // at most 4×
  const near = computeCrop({x: 0, y: 0, w: 600, h: 470}, 640, 480)
  assert.deepEqual(near, {x: 0, y: 0, w: 1, h: 1})   // a face filling the picture: no zoom
})

test('blending moves part of the way', () => {
  const b = blend({x: 0, y: 0, w: 1, h: 1}, {x: 0.2, y: 0.4, w: 0.5, h: 0.5}, 0.5)
  assert.deepEqual(b, {x: 0.1, y: 0.2, w: 0.75, h: 0.75})
})

test('crops from the network are checked', () => {
  assert.deepEqual(cleanCrop([0.1, 0.2, 0.5, 0.5]), {x: 0.1, y: 0.2, w: 0.5, h: 0.5})
  assert.deepEqual(cleanCrop(new Float32Array([0, 0, 1, 1])), {x: 0, y: 0, w: 1, h: 1})
  for (const bad of [null, [1, 2, 3], [0, 0, 0.01, 0.01], [0.8, 0, 0.5, 0.5], [NaN, 0, 1, 1], [0, 0, 2, 1]])
    assert.equal(cleanCrop(bad), null, JSON.stringify(bad))
})
