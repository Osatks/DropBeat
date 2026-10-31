"""Composes "8-Bit Hero": an NES-style chiptune for DropBeat's pixel level, synthesised from scratch.

Channels, as on the NES: pulse 1 (25% duty lead with vibrato), pulse 2 (12.5% duty chord arpeggios),
a 4-bit stepped triangle (octave bass), and LFSR noise (snare / hats) with a triangle-sweep kick.
Pulse waves are band-limited (PolyBLEP) so they stay clean at 44.1 kHz. Pulse 1 sits a little left with a
short echo, pulse 2 a little right: a modern, polished chiptune mix.

Usage: python Tools/make_chiptune.py [out.wav]   (default: Assets/DropBeat/Audio/8-Bit Hero.wav)
"""
import os, sys, wave
import numpy as np

SR = 44100
BPM = 160.0
BEAT = 60.0 / BPM
EIGHTH = BEAT / 2
BAR = BEAT * 4

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'Assets', 'DropBeat', 'Audio', '8-Bit Hero.wav')


def midi_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


# ------------------------------------------------------------------ oscillators

def poly_blep(t, dt):
    y = np.zeros_like(t)
    a = t < dt
    x = t[a] / dt[a]
    y[a] = x + x - x * x - 1.0
    b = t > 1.0 - dt
    x = (t[b] - 1.0) / dt[b]
    y[b] = x * x + x + x + 1.0
    return y


def pulse(freq, n, duty, vib=None):
    """Band-limited pulse wave of n samples. vib: optional per-sample frequency multiplier."""
    f = np.full(n, freq) if vib is None else freq * vib
    dt = f / SR
    phase = np.cumsum(dt) % 1.0
    y = np.where(phase < duty, 1.0, -1.0)
    y += poly_blep(phase, dt)
    y -= poly_blep((phase - duty) % 1.0, dt)
    return y


def triangle4(freq, n):
    """NES triangle: 32-step, 4-bit quantised."""
    phase = (np.arange(n) * freq / SR) % 1.0
    step = np.floor(phase * 32).astype(int)
    level = np.where(step < 16, 15 - step, step - 16)  # 15..0,0..15
    return level / 7.5 - 1.0


def lfsr_noise(n, short=False, rate=22050, seed=1):
    """NES noise channel: 15-bit LFSR clocked at `rate` (short mode = metallic 93-step loop)."""
    out = np.empty(n)
    reg = seed
    tap = 6 if short else 1
    step = SR / rate
    acc = 0.0
    bit = 1.0
    for i in range(n):
        acc += 1.0
        while acc >= step:
            acc -= step
            fb = (reg & 1) ^ ((reg >> tap) & 1)
            reg = (reg >> 1) | (fb << 14)
            bit = 1.0 if (reg & 1) == 0 else -1.0
        out[i] = bit
    return out


_NOISE_CACHE = {}


def noise(n, short=False, rate=22050):
    key = (short, rate)
    if key not in _NOISE_CACHE or len(_NOISE_CACHE[key]) < n:
        _NOISE_CACHE[key] = lfsr_noise(max(n, SR), short, rate)
    return _NOISE_CACHE[key][:n].copy()


def env_adsr(n, a=0.004, d=0.12, s=0.65, r=0.03):
    t = np.arange(n) / SR
    e = np.minimum(1.0, t / max(a, 1e-4))
    e = np.where(t > a, s + (1 - s) * np.exp(-(t - a) / d), e)
    rel = int(r * SR)
    if rel > 0 and n > rel:
        e[-rel:] *= np.linspace(1.0, 0.0, rel)
    return e


# ------------------------------------------------------------------ composition (A minor)

CH = {  # chord tones (MIDI, octave 4) and bass roots (octave 2)
    'Am': ([69, 72, 76], 45), 'F': ([65, 69, 72], 41), 'C': ([60, 64, 67], 48), 'G': ([67, 71, 74], 43),
    'E': ([64, 68, 71], 40), 'Dm': ([62, 65, 69], 38),
}

A4, B4, C5, D5, E5, F5, G5, Gs5, A5, B5, C6, D6, E6, F6 = 69, 71, 72, 74, 76, 77, 79, 80, 81, 83, 84, 86, 88, 89
G4, Gs4, E4 = 67, 68, 64

# Melodies: one list per bar of (start eighth, length in eighths, MIDI note).
VERSE = [
    [(0, 2, E5), (2, 1, A5), (3, 1, G5), (4, 2, E5), (6, 2, C5)],
    [(0, 2, F5), (2, 1, E5), (3, 1, D5), (4, 3, C5), (7, 1, A4)],
    [(0, 1, G4), (1, 1, C5), (2, 2, E5), (4, 2, G5), (6, 2, E5)],
    [(0, 3, D5), (3, 1, B4), (4, 4, G4)],
    [(0, 2, E5), (2, 1, A5), (3, 1, B5), (4, 2, C6), (6, 1, B5), (7, 1, A5)],
    [(0, 2, A5), (2, 1, G5), (3, 1, F5), (4, 2, E5), (6, 2, C5)],
    [(0, 2, D5), (2, 1, E5), (3, 1, F5), (4, 2, G5), (6, 2, B4)],
    [(0, 2, Gs4), (2, 2, B4), (4, 4, E5)],
]
VERSE_CH = ['Am', 'F', 'C', 'G', 'Am', 'F', 'G', 'E']

PRE = [
    [(0, 3, F5), (3, 1, E5), (4, 2, D5), (6, 2, A4)],
    [(0, 3, C5), (3, 1, B4), (4, 4, A4)],
    [(0, 1, A4), (1, 1, C5), (2, 2, F5), (4, 2, E5), (6, 2, C5)],
    [(0, 2, E5), (2, 2, D5), (4, 2, C5), (6, 2, G4)],
    [(0, 2, D5), (2, 2, F5), (4, 3, A5), (7, 1, G5)],
    [(0, 2, Gs5), (2, 1, F5), (3, 1, E5), (4, 2, D5), (6, 2, B4)],
    [(0, 2, C5), (2, 2, E5), (4, 4, A5)],
    [(0, 2, B5), (2, 2, Gs5), (4, 2, E5), (6, 1, D5), (7, 1, B4)],
]
PRE_CH = ['Dm', 'Am', 'F', 'C', 'Dm', 'E', 'Am', 'E']

CHORUS = [
    [(0, 2, A5), (2, 2, C6), (4, 1, A5), (5, 1, G5), (6, 2, F5)],
    [(0, 2, G5), (2, 2, B5), (4, 3, D6), (7, 1, B5)],
    [(0, 3, C6), (3, 1, B5), (4, 2, A5), (6, 2, E5)],
    [(0, 1, A5), (1, 1, B5), (2, 1, C6), (3, 1, D6), (4, 4, E6)],
    [(0, 2, F6), (2, 1, E6), (3, 1, D6), (4, 2, C6), (6, 2, A5)],
    [(0, 2, B5), (2, 1, D6), (3, 1, C6), (4, 2, B5), (6, 2, G5)],
    [(0, 2, C6), (2, 2, G5), (4, 2, E5), (6, 2, G5)],
    [(0, 2, Gs5), (2, 2, B5), (4, 4, E6)],
]
CHORUS_CH = ['F', 'G', 'Am', 'Am', 'F', 'G', 'C', 'E']

INTRO_CH = ['Am', 'F', 'G', 'E']
OUTRO = [[(0, 4, E5), (4, 4, C5)], [(0, 4, D5), (4, 4, B4)], [(0, 8, A4)], [(0, 8, A4)]]
OUTRO_CH = ['Am', 'G', 'Am', 'Am']

# Sections: (name, chords, melody or None, drum style, arp on)
SONG = [
    ('intro', INTRO_CH, None, 'light', True),
    ('verse', VERSE_CH, VERSE, 'groove', True),
    ('pre', PRE_CH, PRE, 'groove+', True),
    ('chorus', CHORUS_CH, CHORUS, 'drive', True),
    ('break', INTRO_CH, None, 'break', True),
    ('chorus', CHORUS_CH, CHORUS, 'drive', True),
    ('verse', VERSE_CH, VERSE, 'groove', True),
    ('chorus', CHORUS_CH, CHORUS, 'drive', True),
    ('outro', OUTRO_CH, OUTRO, 'end', True),
]


# ------------------------------------------------------------------ rendering

def add(buf, start, sig, gain):
    i = int(round(start * SR))
    if i >= len(buf):
        return
    end = min(len(buf), i + len(sig))
    buf[i:end] += sig[:end - i] * gain


def lead_note(midi, seconds):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    vib = 1.0 + 0.012 * np.sin(2 * np.pi * 6.0 * t) * np.clip((t - 0.15) / 0.1, 0, 1)
    return pulse(midi_hz(midi), n, 0.25, vib) * env_adsr(n, 0.004, 0.15, 0.7, 0.025)


def arp_chord(tones, seconds):
    """Classic chip arpeggio: chord tones cycled every 1/40 s on a thin 12.5% pulse."""
    n = int(seconds * SR)
    seg = int(SR / 40)
    y = np.zeros(n)
    for k in range(0, n, seg):
        m = tones[(k // seg) % len(tones)]
        L = min(seg, n - k)
        y[k:k + L] = pulse(midi_hz(m), L, 0.125)
    return y * env_adsr(n, 0.003, 0.4, 0.55, 0.02)


def bass_note(midi, seconds):
    n = int(seconds * SR)
    y = triangle4(midi_hz(midi), n)
    rel = int(0.01 * SR)
    if n > rel:
        y[-rel:] *= np.linspace(1, 0, rel)
    return y


def kick():
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    f = 50 + 140 * np.exp(-t / 0.025)
    ph = np.cumsum(f / SR) % 1.0
    tri = 1.0 - 4.0 * np.abs(ph - 0.5)
    return tri * np.exp(-t / 0.06) + noise(n, rate=44100) * 0.25 * np.exp(-t / 0.004)


def snare():
    n = int(0.16 * SR)
    t = np.arange(n) / SR
    tone = triangle4(190.0, n) * np.exp(-t / 0.03) * 0.5
    return noise(n, rate=33000) * np.exp(-t / 0.05) + tone


def hat(open_=False):
    n = int((0.09 if open_ else 0.035) * SR)
    t = np.arange(n) / SR
    return noise(n, short=True, rate=44100) * np.exp(-t / (0.03 if open_ else 0.01))


def riser(seconds):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    rates = np.linspace(4000, 44100, 16)
    seg = n // 16
    for k in range(16):
        out[k * seg:(k + 1) * seg] = noise(seg, rate=int(rates[k]))
    return out * (t / seconds) ** 2


def drums(buf, bar_start, style, last_bar):
    K, S = kick(), snare()
    for beat in range(4):
        t0 = bar_start + beat * BEAT
        if style == 'light':
            if beat == 0:
                add(buf, t0, K, 0.9)
            add(buf, t0 + EIGHTH, hat(), 0.18)
        elif style in ('groove', 'groove+'):
            if beat in (0, 2):
                add(buf, t0, K, 0.95)
            if beat in (1, 3):
                add(buf, t0, S, 0.55)
            add(buf, t0, hat(), 0.14)
            add(buf, t0 + EIGHTH, hat(style == 'groove+' and beat == 3), 0.2)
            if beat == 2 and style == 'groove+':
                add(buf, t0 + EIGHTH, K, 0.6)
        elif style == 'drive':
            add(buf, t0, K, 1.0)
            if beat in (1, 3):
                add(buf, t0, S, 0.6)
            for s in range(4):
                add(buf, t0 + s * BEAT / 4, hat(s == 2), 0.13 if s != 2 else 0.17)
        elif style == 'break':
            add(buf, t0, K, 0.8)
        elif style == 'end':
            if beat == 0:
                add(buf, t0, K, 0.9)
                add(buf, t0, hat(True), 0.2)
    if last_bar and style not in ('end', 'light'):
        # Snare fill on the last beat of a section.
        for s in range(4):
            add(buf, bar_start + 3 * BEAT + s * BEAT / 4, S, 0.35 + 0.08 * s)


def render():
    bars = sum(len(sec[1]) for sec in SONG)
    total = bars * BAR + 2.0
    n = int(total * SR)
    lead = np.zeros(n)
    arp = np.zeros(n)
    bass = np.zeros(n)
    drum = np.zeros(n)

    bar = 0
    for name, chords, melody, style, arp_on in SONG:
        for bi, chord in enumerate(chords):
            t_bar = bar * BAR + 0.25  # short lead-in of silence
            tones, root = CH[chord]
            last = bi == len(chords) - 1
            # Bass: octave-jumping eighths (triangle), held in the break and outro ends.
            if style == 'break' or (style == 'end' and bi >= 2):
                add(bass, t_bar, bass_note(root + 12, BAR * 0.98), 0.5)
            else:
                for e in range(8):
                    m = root + (12 if e % 2 else 0)
                    add(bass, t_bar + e * EIGHTH, bass_note(m, EIGHTH * 0.92), 0.5)
            if arp_on:
                arp_len = BAR if style != 'end' or bi < 3 else BAR * 2
                add(arp, t_bar, arp_chord(tones, arp_len * 0.98), 0.17 if style == 'drive' else 0.2)
            if melody is not None:
                for start, length, m in melody[bi]:
                    seconds = length * EIGHTH * 0.94
                    add(lead, t_bar + start * EIGHTH, lead_note(m, seconds), 0.3)
                    if style == 'drive':  # chorus: a softer octave-down double for weight
                        add(arp, t_bar + start * EIGHTH, pulse(midi_hz(m - 12), int(seconds * SR), 0.5) * env_adsr(int(seconds * SR), 0.004, 0.1, 0.5, 0.02), 0.07)
            drums(drum, t_bar, style, last)
            if style == 'break' and last:
                add(drum, t_bar, riser(BAR), 0.22)
            bar += 1
    # Final hit.
    end_t = bar * BAR + 0.25
    add(drum, end_t, kick(), 1.0)
    add(drum, end_t, hat(True), 0.25)
    add(bass, end_t, bass_note(45, 1.2) * np.exp(-np.arange(int(1.2 * SR)) / SR / 0.5), 0.5)
    add(arp, end_t, arp_chord([69, 72, 76, 81], 1.4), 0.2)

    # Lead echo (3/16 note, two repeats).
    d = int(BEAT * 0.75 * SR)
    echo = np.zeros(n)
    echo[d:] += lead[:-d] * 0.28
    echo[2 * d:] += lead[:-2 * d] * 0.12

    left = lead * 0.8 + echo * 0.5 + arp * 0.55 + bass + drum
    right = lead * 0.55 + echo * 0.75 + arp * 0.85 + bass + drum
    st = np.stack([left, right], axis=1)
    peak = np.max(np.abs(st))
    st = np.tanh(st / peak * 1.25) / np.tanh(1.25) * 0.89  # gentle limiter
    return st


def main():
    st = render()
    out_dir = os.path.dirname(os.path.abspath(OUT))
    os.makedirs(out_dir, exist_ok=True)
    pcm = (st * 32767).astype(np.int16)
    tmp = os.path.join(out_dir, '.chiptune.wav.part')
    with wave.open(tmp, 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    os.replace(tmp, OUT)
    print(f'wrote {OUT}: {len(st) / SR:.1f} s, {BPM:.0f} BPM')


if __name__ == '__main__':
    main()
