# Test fixtures

`face.mjpeg` is what the end-to-end test's fake cameras show (Chrome's `--use-file-for-fake-video-capture`; a single
JPEG is a one-frame MJPEG that Chrome loops): a head-and-shoulders portrait left of centre on a plain background, so
the face zoom has a real face to find and must move off-centre to frame it.

The portrait is NASA's official portrait of astronaut Mae C. Jemison (NASA Image and Video Library), public domain, from
[Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Mae_Carol_Jemison.jpg). Made with:

```bash
ffmpeg -f lavfi -i "color=c=0x6f8fa8:s=640x480" -i Mae_Carol_Jemison_500px.jpg \
  -filter_complex "[1:v]crop=360:340:100:0,scale=-1:380[p];[0:v][p]overlay=30:100" -frames:v 1 -q:v 4 face.mjpeg
```
