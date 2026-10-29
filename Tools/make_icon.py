"""DropBeat app icon: chrome ball dropping into a segmented neon ring, synthwave sun behind.
Rendered at 2x then downsampled for clean edges. iOS icons must be opaque squares (iOS rounds them)."""
import sys
import numpy as np
from PIL import Image, ImageFilter

S = 2048
out = sys.argv[1]
y, x = np.mgrid[0:S, 0:S].astype(np.float32) / S  # 0..1, y down

def smooth(e0, e1, v):
    t = np.clip((v - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)

def blur(img, r):
    ch = [np.asarray(Image.fromarray(np.clip(img[..., i] * 60, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), np.float32) / 60 for i in range(3)]
    return np.stack(ch, -1)

col = np.zeros((S, S, 3), np.float32)

# --- night sky gradient + nebula glow
top = np.array([0.03, 0.01, 0.10]); bot = np.array([0.20, 0.03, 0.30])
g = y[..., None]
col += top * (1 - g) + bot * g
neb = np.exp(-(((x - 0.5) / 0.42) ** 2 + ((y - 0.55) / 0.30) ** 2))
col += neb[..., None] * np.array([0.35, 0.05, 0.45]) * 0.55

# --- synthwave sun
cx, cy, R = 0.5, 0.50, 0.30
d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
disk = 1 - smooth(R - 0.002, R + 0.002, d)
v = np.clip((y - (cy - R)) / (2 * R), 0, 1)  # 0 top .. 1 bottom of the sun
sun = np.array([1.0, 0.72, 0.12])[None, None] * (1 - v[..., None]) + np.array([1.0, 0.08, 0.55])[None, None] * v[..., None]
# stripes cut in the lower half, gaps grow downward
yy = (y - cy) / R
stripe = np.ones_like(x)
lower = yy > -0.05
bands = np.mod(yy * 7.5, 1.0)
gap = np.clip((yy + 0.05) * 0.55, 0, 0.6)
stripe[lower] = (bands[lower] > gap[lower]).astype(np.float32)
col += sun * (disk * stripe)[..., None] * 0.85
halo = np.exp(-np.maximum(d - R, 0) * 14) * (d > R)
col += halo[..., None] * np.array([0.9, 0.2, 0.55]) * 0.35

# --- perspective grid floor
horizon = 0.80
floor = y > horizon
depth = np.where(floor, (y - horizon) / (1 - horizon), 0)
grid = np.zeros_like(x)
z = 1.0 / np.maximum(depth, 1e-3)
hl = np.abs(np.mod(z * 0.9, 1.0) - 0.5)
vl = np.abs(np.mod((x - 0.5) * z * 2.4 + 0.5, 1.0) - 0.5)
lw = 0.03
grid = np.maximum(1 - smooth(0, lw * z * 0.6 + 0.01, 0.5 - np.abs(hl - 0.5) * 0 + (0.5 - hl) * 0), 0)
grid = (1 - smooth(0.0, 0.035 * z ** 0.5, 0.5 - hl)) * 0 + (hl > 0.5 - 0.02 * z).astype(np.float32)
grid = np.maximum(grid, (vl > 0.5 - 0.012 * z).astype(np.float32))
grid *= floor * np.clip(depth * 3, 0, 1)
col = col * (1 - 0.5 * floor[..., None]) + np.array([0.02, 0.0, 0.05]) * floor[..., None] * 0.5
col += grid[..., None] * np.array([1.0, 0.25, 0.9]) * 0.55

# --- neon ring (ellipse = tilted ring), split into back half and front half
rcx, rcy, rx, ry, th = 0.5, 0.665, 0.39, 0.13, 0.03
ex = (x - rcx) / rx; ey = (y - rcy) / ry
er = np.sqrt(ex ** 2 + ey ** 2)
# distance to the ellipse curve, approx in pixels of the smaller radius
grad = np.sqrt((ex / rx) ** 2 + (ey / ry) ** 2) / np.maximum(er, 1e-3)
dist = np.abs(er - 1) / np.maximum(grad, 1e-3)
dist = dist * (1.0 - 0.35 * np.clip(ey, 0, 1))  # front of the ring a bit thicker (closer)
band = 1 - smooth(th * 0.5, th * 0.5 + 0.003, dist)
ang = np.arctan2(ex, -ey) / (2 * np.pi) % 1.0
segs = 12
f = np.mod(ang * segs, 1.0)
seg = smooth(0.06, 0.09, f) * smooth(0.06, 0.09, 1 - f)
ringmask = band * seg
front = ey > 0
cyan = np.array([0.25, 1.0, 1.0]); pink = np.array([1.0, 0.3, 0.95])
ringcol = cyan[None, None] * (1 - np.clip(ang, 0, 1)[..., None] * 0.0)
# charged segments (most of the ring) white-hot cyan, last few magenta
lit = (ang < 0.78)[..., None]
ringcol = np.where(lit, np.array([0.15, 0.95, 1.3]), np.array([1.4, 0.2, 1.1]))

def add_ring(mask):
    global col
    glow = blur(ringcol * mask[..., None], 48) * 2.6 + blur(ringcol * mask[..., None], 14) * 1.6
    col = col + glow
    core = ringcol * 1.3 + np.array([0.55, 0.55, 0.55]) * smooth(0.0, 1.0, mask)[..., None] ** 4
    col = col * (1 - mask[..., None]) + core * mask[..., None]

add_ring(ringmask * (~front))

# --- chrome ball
bcx, bcy, br = 0.5, 0.50, 0.185
bx = (x - bcx) / br; by = (y - bcy) / br
r2 = bx ** 2 + by ** 2
inside = r2 < 1
nz = np.sqrt(np.clip(1 - r2, 0, 1))
# reflection direction ~ normal; environment: sky above, bright horizon band, pink/cyan floor below
# Horizon reflection curves down toward the edges, which is what makes chrome read as a sphere.
t = by - 0.28 * bx ** 2 + 0.06  # <0 sky, >0 floor
sky_top = np.array([0.05, 0.04, 0.18]); sky_low = np.array([0.45, 0.16, 0.75])
hor = np.array([1.5, 1.35, 1.4])
gold = np.array([1.2, 0.62, 0.15]); pink = np.array([0.9, 0.08, 0.5]); deep = np.array([0.25, 0.02, 0.22])
s_up = np.clip(-t / 0.9, 0, 1)[..., None]
sky = sky_low * (1 - s_up) + sky_top * s_up
s_dn = np.clip(t / 0.85, 0, 1)[..., None]
floorc = np.where(s_dn < 0.35, gold + (pink - gold) * (s_dn / 0.35), pink + (deep - pink) * ((s_dn - 0.35) / 0.65))
line = np.exp(-(t / 0.035) ** 2)[..., None]
envc = np.where((t < 0)[..., None], sky, floorc) + hor * line
# cyan rim light from the ring below, magenta from the sides (fresnel)
fres = (1 - nz) ** 2.5
envc = envc * (0.35 + 0.65 * nz[..., None] ** 0.6)
envc = envc + fres[..., None] * np.array([0.15, 0.85, 1.1]) * np.clip(by + 0.2, 0, 1)[..., None] * 1.2
envc = envc + fres[..., None] * np.array([0.7, 0.15, 0.9]) * np.clip(-by + 0.2, 0, 1)[..., None] * 0.5
# specular highlights
spec = np.exp(-(((bx + 0.36) / 0.15) ** 2 + ((by + 0.48) / 0.09) ** 2)) * 2.4
spec2 = np.exp(-(((bx - 0.5) / 0.06) ** 2 + ((by - 0.42) / 0.05) ** 2)) * 0.9
ballc = envc + (spec + spec2)[..., None]
edge = 1 - smooth(0.985, 1.0, np.sqrt(r2))
# soft shadow/glow behind the ball and a speed trail above it
trail = np.exp(-((x - bcx) / 0.05) ** 2) * smooth(bcy - 0.42, bcy - 0.05, y) * (y < bcy)
col += trail[..., None] * np.array([0.3, 0.9, 1.0]) * 0.35
col += blur(np.array([0.1, 0.5, 1.0])[None, None] * edge[..., None], 30) * 0.35
col = col * (1 - edge[..., None]) + ballc * edge[..., None]

add_ring(ringmask * front)

# --- tone map + vignette
vig = 1 - 0.35 * (((x - 0.5) ** 2 + (y - 0.5) ** 2) * 2)
col *= vig[..., None]
col = np.where(col < 0.8, col, 0.8 + 0.2 * (1 - np.exp(-(col - 0.8) / 0.2)))
img = Image.fromarray((np.clip(col, 0, 1) ** (1 / 1.1) * 255).astype(np.uint8), 'RGB')
img = img.resize((1024, 1024), Image.LANCZOS)
img.save(out)
print('saved', out)
