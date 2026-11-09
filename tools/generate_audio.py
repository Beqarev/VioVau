#!/usr/bin/env python3
"""Synthesize Violet Vaults music + SFX (art bible Batch 5).

Music: D minor, 70 bpm exploration layers (seamless loops); Collapse at 140 bpm.
SFX: retro 16-bit style, mono WAV, peak-normalized.
Outputs to game/assets/audio/{music,sfx}; raw wav copies under game/assets/raw/audio/.
"""

from __future__ import annotations

import math
import struct
import subprocess
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
MUSIC_OUT = ROOT / "game/assets/audio/music"
SFX_OUT = ROOT / "game/assets/audio/sfx"
RAW_OUT = ROOT / "game/assets/raw/audio"
SR = 44100
RNG = np.random.default_rng(777)

# D natural minor
NOTE = {
    "D2": 73.42, "A2": 110.00, "D3": 146.83, "F3": 174.61, "A3": 220.00,
    "Bb3": 233.08, "C4": 261.63, "D4": 293.66, "E4": 329.63, "F4": 349.23,
    "G4": 392.00, "A4": 440.00, "Bb4": 466.16, "C5": 523.25, "D5": 587.33,
    "E5": 659.25, "F5": 698.46, "A5": 880.00,
}


def beat(bpm: float) -> float:
    return 60.0 / bpm


def env(n: int, a: float = 0.01, r: float = 0.05) -> np.ndarray:
    e = np.ones(n, dtype=np.float64)
    a_n = max(1, int(a * SR))
    r_n = max(1, int(r * SR))
    e[:a_n] *= np.linspace(0, 1, a_n)
    e[-r_n:] *= np.linspace(1, 0, r_n)
    return e


def sine(freq: float, dur: float, vol: float = 1.0, phase: float = 0.0) -> np.ndarray:
    t = np.arange(int(dur * SR)) / SR
    return np.sin(2 * np.pi * freq * t + phase) * vol


def triangle(freq: float, dur: float, vol: float = 1.0) -> np.ndarray:
    t = np.arange(int(dur * SR)) / SR
    return (2 / np.pi) * np.arcsin(np.sin(2 * np.pi * freq * t)) * vol


def square(freq: float, dur: float, vol: float = 1.0, duty: float = 0.5) -> np.ndarray:
    t = np.arange(int(dur * SR)) / SR
    return np.where((t * freq) % 1.0 < duty, vol, -vol)


def noise(dur: float, vol: float = 1.0) -> np.ndarray:
    return RNG.uniform(-1, 1, int(dur * SR)) * vol


def soft_clip(x: np.ndarray, drive: float = 1.2) -> np.ndarray:
    return np.tanh(x * drive) / math.tanh(drive)


def normalize(x: np.ndarray, peak: float = 0.9) -> np.ndarray:
    m = np.max(np.abs(x)) + 1e-12
    return x * (peak / m)


def fade_edges(x: np.ndarray, sec: float = 0.02) -> np.ndarray:
    n = max(1, int(sec * SR))
    x = x.copy()
    if len(x) < 2 * n:
        return x
    x[:n] *= np.linspace(0, 1, n)
    x[-n:] *= np.linspace(1, 0, n)
    return x


def write_wav(path: Path, samples: np.ndarray, mono: bool = True, peak: float = 0.9) -> None:
    samples = normalize(samples.astype(np.float64), peak)
    pcm = np.clip(samples * 32767, -32767, 32767).astype(np.int16)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "w") as w:
        w.setnchannels(1 if mono else 2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def wav_to_ogg(wav: Path, ogg: Path) -> None:
    ogg.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        ["ffmpeg", "-y", "-i", str(wav), "-c:a", "libvorbis", "-q:a", "5", str(ogg)],
        check=True, capture_output=True,
    )


def place(buf: np.ndarray, clip: np.ndarray, at_sec: float) -> None:
    i = int(at_sec * SR)
    if i >= len(buf):
        return
    end = min(len(buf), i + len(clip))
    buf[i:end] += clip[: end - i]


def music_box_pluck(freq: float, dur: float = 0.9, vol: float = 0.35) -> np.ndarray:
    # Bright decaying partials — music-box / glass feel.
    partials = [(1, 1.0), (2.003, 0.45), (3.01, 0.22), (4.02, 0.1), (5.05, 0.05)]
    n = int(dur * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for mul, amp in partials:
        out += np.sin(2 * np.pi * freq * mul * t) * amp * np.exp(-t * (2.8 + mul * 0.6))
    return out * vol * env(n, 0.002, 0.15)


def glass_pad(freq: float, dur: float, vol: float = 0.12) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    # Slow beating pair of sines.
    a = np.sin(2 * np.pi * freq * t)
    b = np.sin(2 * np.pi * (freq * 1.003) * t + 0.3)
    return soft_clip((a + b) * 0.5 * vol) * env(n, 0.4, 0.6)


def drone(freqs: list[float], dur: float, vol: float = 0.2) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for i, f in enumerate(freqs):
        out += np.sin(2 * np.pi * f * t + i) * (1.0 / len(freqs))
        out += np.sin(2 * np.pi * f * 0.5 * t) * 0.15 / len(freqs)
    # Subtle breath.
    out *= 1.0 + 0.04 * np.sin(2 * np.pi * 0.08 * t)
    return out * vol * env(n, 0.8, 0.8)


def heartbeat(dur: float, bpm: float, vol: float = 0.18) -> np.ndarray:
    n = int(dur * SR)
    out = np.zeros(n)
    interval = beat(bpm) * 2  # half-note pulse
    t0 = 0.0
    while t0 < dur:
        thump = noise(0.12, 1.0) * np.exp(-np.linspace(0, 18, int(0.12 * SR)))
        body = sine(48, 0.12, 1.0) * np.exp(-np.linspace(0, 12, int(0.12 * SR)))
        n = min(len(thump), len(body))
        hit = normalize(thump[:n] * 0.4 + body[:n] * 0.9) * vol
        place(out, hit, t0)
        place(out, hit * 0.55, t0 + 0.16)
        t0 += interval
    return out


def string_creep(dur: float, bpm: float, denser: bool, vol: float = 0.1) -> np.ndarray:
    n = int(dur * SR)
    out = np.zeros(n)
    motif = ["D3", "F3", "A3", "Bb3", "A3", "F3"]
    step = beat(bpm) * (1 if denser else 2)
    t0 = 0.0
    i = 0
    while t0 < dur - 0.2:
        f = NOTE[motif[i % len(motif)]]
        det = triangle(f * 0.997, 1.4, vol) + triangle(f * 1.005, 1.4, vol * 0.7)
        place(out, det * env(len(det), 0.2, 0.5), t0)
        t0 += step
        i += 1
    return out


def metallic_perc(dur: float, bpm: float, vol: float = 0.12) -> np.ndarray:
    n = int(dur * SR)
    out = np.zeros(n)
    step = beat(bpm)
    t0 = 0.0
    k = 0
    while t0 < dur:
        if k % 4 in (0, 2, 3):
            click = noise(0.04, 1.0) * np.exp(-np.linspace(0, 40, int(0.04 * SR)))
            ring = sine(NOTE["A5"] * (1.0 + 0.02 * (k % 3)), 0.25, 0.5) * np.exp(-np.linspace(0, 10, int(0.25 * SR)))
            place(out, normalize(click[: min(len(click), len(ring))] * 0.5 + ring[:len(click)] * 0.5) * vol, t0)
        t0 += step
        k += 1
    return out


def ostinato(dur: float, bpm: float, vol: float = 0.14) -> np.ndarray:
    n = int(dur * SR)
    out = np.zeros(n)
    pattern = ["D4", "A3", "D4", "F4", "E4", "D4", "C4", "A3"]
    step = beat(bpm) / 2  # eighths
    t0 = 0.0
    i = 0
    while t0 < dur - 0.05:
        f = NOTE[pattern[i % len(pattern)]]
        note = square(f, 0.18, vol * 0.35, duty=0.3) * env(int(0.18 * SR), 0.005, 0.08)
        place(out, note, t0)
        t0 += step
        i += 1
    return out


def brass_stab(freq: float, dur: float = 0.55, vol: float = 0.35) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    wave = (
        np.sin(2 * np.pi * freq * t)
        + 0.5 * np.sin(2 * np.pi * freq * 2 * t)
        + 0.25 * np.sin(2 * np.pi * freq * 3 * t)
        + 0.12 * np.sin(2 * np.pi * freq * 4.02 * t)
    )
    return soft_clip(wave * vol) * env(n, 0.01, 0.25)


def choir_cluster(dur: float, vol: float = 0.12) -> np.ndarray:
    # Dissonant cluster around D.
    freqs = [NOTE["D3"] * 0.97, NOTE["D3"], NOTE["F3"] * 1.02, NOTE["A3"] * 0.98, NOTE["Bb3"]]
    return drone(freqs, dur, vol)


def exploration_layer(kind: str, bars: int = 16, bpm: float = 70.0) -> np.ndarray:
    """Shared D minor / 70 bpm bed; layers add tension for crossfading."""
    dur = bars * 4 * beat(bpm)
    buf = drone([NOTE["D2"], NOTE["A2"], NOTE["D3"]], dur, 0.22)

    # Sparse music-box motif (same seed pattern on all layers so they lock).
    motif = ["D4", "A4", "F4", "E4", "D4", "A3", "Bb3", "A3", "F4", "D4", "C4", "A3"]
    step = beat(bpm) * 2
    t0 = beat(bpm) * 2
    for i, name in enumerate(motif * 4):
        if t0 >= dur - 1:
            break
        # Skip some notes on Calm for space.
        if kind == "calm" and i % 3 == 1:
            t0 += step
            continue
        vol = 0.22 if kind == "calm" else 0.18 if kind == "restless" else 0.14
        place(buf, music_box_pluck(NOTE[name], 1.1, vol), t0)
        if kind != "calm" and i % 4 == 0:
            place(buf, glass_pad(NOTE[name] / 2, 2.5, 0.08), t0)
        t0 += step

    # Occasional distant chime.
    for bar in range(0, bars, 4):
        place(buf, music_box_pluck(NOTE["A5"], 1.6, 0.12), bar * 4 * beat(bpm) + beat(bpm) * 3)

    if kind in ("restless", "hunting"):
        buf += heartbeat(dur, bpm, 0.14 if kind == "restless" else 0.1)
        buf += string_creep(dur, bpm, denser=(kind == "hunting"), vol=0.09 if kind == "restless" else 0.12)

    if kind == "hunting":
        buf += ostinato(dur, bpm, 0.11)
        buf += metallic_perc(dur, bpm, 0.1)

    # Seamless: crossfade last 0.5s with first 0.5s.
    xfade = int(0.5 * SR)
    if len(buf) > 2 * xfade:
        head = buf[:xfade].copy()
        buf[-xfade:] = buf[-xfade:] * np.linspace(1, 0, xfade) + head * np.linspace(0, 1, xfade)
        buf[:xfade] = buf[-xfade:]
    return normalize(soft_clip(buf), 0.85)


def menu_track(bars: int = 12, bpm: float = 70.0) -> np.ndarray:
    dur = bars * 4 * beat(bpm)
    buf = drone([NOTE["D2"], NOTE["A2"], NOTE["F3"]], dur, 0.18)
    melody = ["D4", "F4", "A4", "G4", "F4", "E4", "D4", "A3", "Bb3", "A3", "F4", "D4"]
    t0 = 0.5
    for i, name in enumerate(melody * 3):
        if t0 >= dur - 1:
            break
        place(buf, music_box_pluck(NOTE[name], 1.3, 0.28), t0)
        place(buf, glass_pad(NOTE[name] / 2, 2.0, 0.07), t0)
        t0 += beat(bpm) * (2 if i % 5 != 4 else 3)
    xfade = int(0.4 * SR)
    buf[-xfade:] = buf[-xfade:] * np.linspace(1, 0, xfade) + buf[:xfade] * np.linspace(0, 1, xfade)
    return normalize(soft_clip(buf), 0.85)


def warden_layer(bars: int = 12, bpm: float = 70.0) -> np.ndarray:
    dur = bars * 4 * beat(bpm)
    buf = choir_cluster(dur, 0.16)
    buf += drone([NOTE["D2"] * 0.75, NOTE["D2"]], dur, 0.2)
    # Brass stabs every 2 bars.
    for bar in range(0, bars, 2):
        t = bar * 4 * beat(bpm)
        place(buf, brass_stab(NOTE["D3"], 0.7, 0.4), t)
        place(buf, brass_stab(NOTE["F3"], 0.55, 0.28), t + beat(bpm))
    # Slow drums.
    t0 = 0.0
    while t0 < dur:
        boom = sine(40, 0.35, 0.5) * np.exp(-np.linspace(0, 8, int(0.35 * SR)))
        place(buf, boom, t0)
        place(buf, noise(0.05, 0.2) * np.exp(-np.linspace(0, 30, int(0.05 * SR))), t0)
        t0 += beat(bpm) * 4
    xfade = int(0.4 * SR)
    buf[-xfade:] = buf[-xfade:] * np.linspace(1, 0, xfade) + buf[:xfade] * np.linspace(0, 1, xfade)
    return normalize(soft_clip(buf), 0.88)


def collapse_track(bars: int = 16, bpm: float = 140.0) -> np.ndarray:
    dur = bars * 4 * beat(bpm)
    buf = drone([NOTE["D2"], NOTE["A2"]], dur, 0.15)
    # Driving drums.
    t0 = 0.0
    k = 0
    while t0 < dur:
        kick = sine(55, 0.12, 0.55) * np.exp(-np.linspace(0, 20, int(0.12 * SR)))
        place(buf, kick, t0)
        if k % 2 == 1:
            snare = noise(0.08, 0.35) * np.exp(-np.linspace(0, 25, int(0.08 * SR)))
            place(buf, snare, t0)
        if k % 4 == 2:
            place(buf, noise(0.03, 0.2) * np.exp(-np.linspace(0, 50, int(0.03 * SR))), t0 + beat(bpm) * 0.5)
        t0 += beat(bpm)
        k += 1
    # Rising strings / crumbling.
    for i in range(bars):
        f = NOTE["D3"] * (1 + i * 0.015)
        place(buf, triangle(f, beat(bpm) * 4, 0.08) * env(int(beat(bpm) * 4 * SR), 0.05, 0.2), i * 4 * beat(bpm))
        place(buf, noise(0.2, 0.05 + i * 0.004) * env(int(0.2 * SR), 0.01, 0.1), i * 4 * beat(bpm) + beat(bpm) * 2)
    # Urgent ostinato doubled.
    buf += ostinato(dur, bpm, 0.12)
    xfade = int(0.3 * SR)
    buf[-xfade:] = buf[-xfade:] * np.linspace(1, 0, xfade) + buf[:xfade] * np.linspace(0, 1, xfade)
    return normalize(soft_clip(buf), 0.9)


def victory_sting() -> np.ndarray:
    notes = ["D4", "F4", "A4", "D5", "A4", "F5"]
    buf = np.zeros(int(10 * SR))
    t = 0.15
    for i, name in enumerate(notes):
        place(buf, music_box_pluck(NOTE[name], 1.4, 0.35 - i * 0.02), t)
        place(buf, glass_pad(NOTE[name] / 2, 2.5, 0.1), t)
        t += 0.35 if i < 3 else 0.55
    place(buf, drone([NOTE["D3"], NOTE["A3"], NOTE["D4"]], 4.0, 0.12), 2.0)
    return fade_edges(normalize(buf), 0.05)


def death_sting() -> np.ndarray:
    notes = ["D5", "A4", "F4", "D4", "A3", "D3", "D2"]
    buf = np.zeros(int(6.5 * SR))
    t = 0.1
    for i, name in enumerate(notes):
        # Winding-down music box: slower and quieter.
        place(buf, music_box_pluck(NOTE[name], 1.2 + i * 0.15, 0.32 - i * 0.03), t)
        t += 0.45 + i * 0.12
    place(buf, drone([NOTE["D2"]], 3.0, 0.15), 2.5)
    return fade_edges(normalize(buf), 0.05)


# ── SFX ──────────────────────────────────────────────────────────────────────

def sfx_blip(freq: float, dur: float, vol: float = 0.5, kind: str = "square") -> np.ndarray:
    gen = square if kind == "square" else triangle if kind == "tri" else sine
    return fade_edges(normalize(gen(freq, dur, vol) * env(int(dur * SR), 0.005, max(0.02, dur * 0.4))), 0.005)


def lowpass(x: np.ndarray, cutoff_hz: float) -> np.ndarray:
    """Simple one-pole low-pass for soft dungeon grit (not a chip-tune blip)."""
    if len(x) == 0:
        return x
    rc = 1.0 / (2 * math.pi * cutoff_hz)
    dt = 1.0 / SR
    a = dt / (rc + dt)
    y = np.empty_like(x)
    acc = 0.0
    for i, sample in enumerate(x):
        acc += a * (sample - acc)
        y[i] = acc
    return y


def stone_footstep(heavy: bool) -> np.ndarray:
    """Muted stone/dust step: soft body thump + filtered grit. Quiet, short, blocky."""
    dur = 0.09 if heavy else 0.07
    n = int(dur * SR)
    t = np.linspace(0, 1, n)

    # Soft low body (boot on stone), not a sine beep.
    body_f = 72 if heavy else 88
    body = np.sin(2 * np.pi * body_f * np.arange(n) / SR) * np.exp(-t * (14 if heavy else 18))
    body += 0.35 * np.sin(2 * np.pi * (body_f * 0.5) * np.arange(n) / SR) * np.exp(-t * 20)

    # Dust/gravel grit — band-limited noise, Minecraft-adjacent scuff.
    grit = lowpass(RNG.uniform(-1, 1, n), 1800 if heavy else 1400)
    grit *= np.exp(-t * (22 if heavy else 28))
    # Tiny early transient so the step reads.
    click = lowpass(RNG.uniform(-1, 1, n), 3200) * np.exp(-t * 55) * 0.35

    mix = body * (0.55 if heavy else 0.4) + grit * (0.45 if heavy else 0.35) + click * 0.25
    return fade_edges(normalize(mix, peak=0.55 if heavy else 0.42), 0.003)


def make_sfx() -> dict[str, np.ndarray]:
    out: dict[str, np.ndarray] = {}

    # Footsteps — soft stone/dust thuds (Minecraft-adjacent feel, violet-dungeon grit).
    out["footstep_walk"] = stone_footstep(heavy=False)
    out["footstep_sprint"] = stone_footstep(heavy=True)

    def stack(*clips: np.ndarray) -> np.ndarray:
        n = max(len(c) for c in clips)
        buf = np.zeros(n)
        for c in clips:
            buf[: len(c)] += c
        return fade_edges(normalize(buf))

    def seq(*clips: np.ndarray) -> np.ndarray:
        return fade_edges(normalize(np.concatenate(clips)))

    out["chest_open"] = seq(
        noise(0.04, 0.4) * np.exp(-np.linspace(0, 20, int(0.04 * SR))),
        square(NOTE["A4"], 0.08, 0.25) * env(int(0.08 * SR), 0.005, 0.04),
        music_box_pluck(NOTE["D5"], 0.35, 0.35),
    )
    out["key_pickup"] = stack(music_box_pluck(NOTE["A5"], 0.25, 0.4), music_box_pluck(NOTE["D5"], 0.2, 0.25) * 0.5)
    out["door_unlock"] = seq(
        square(NOTE["D3"], 0.06, 0.3) * env(int(0.06 * SR), 0.002, 0.03),
        noise(0.05, 0.35) * np.exp(-np.linspace(0, 25, int(0.05 * SR))),
        music_box_pluck(NOTE["F4"], 0.4, 0.3),
    )

    # Card categories — short chip motifs
    out["card_mobility"] = stack(sfx_blip(NOTE["A4"], 0.08, 0.45), np.pad(sfx_blip(NOTE["D5"], 0.1, 0.4), (int(0.07 * SR), 0)))
    out["card_stealth"] = fade_edges(normalize(sine(NOTE["A3"], 0.25, 0.35) * np.exp(-np.linspace(0, 8, int(0.25 * SR)))), 0.01)
    out["card_utility"] = stack(sfx_blip(NOTE["F4"], 0.1, 0.4, "tri"), np.pad(sfx_blip(NOTE["A4"], 0.1, 0.35, "tri"), (int(0.08 * SR), 0)))
    out["card_combat"] = stack(
        square(NOTE["D3"], 0.07, 0.45) * env(int(0.07 * SR), 0.002, 0.03),
        noise(0.08, 0.3) * np.exp(-np.linspace(0, 30, int(0.08 * SR))),
    )
    out["card_greed"] = stack(music_box_pluck(NOTE["F5"], 0.2, 0.35), music_box_pluck(NOTE["A5"], 0.2, 0.3))
    out["card_social"] = stack(
        sfx_blip(NOTE["F4"], 0.09, 0.35),
        np.pad(sfx_blip(NOTE["A4"], 0.09, 0.35), (int(0.1 * SR), 0)),
        np.pad(sfx_blip(NOTE["D5"], 0.1, 0.3), (int(0.2 * SR), 0)),
    )

    # Noise ring — expands with layered whoosh (volume scaled in-game by amount)
    out["noise_ring"] = stack(
        noise(0.35, 0.5) * np.exp(-np.linspace(0, 6, int(0.35 * SR))),
        sine(120, 0.35, 0.25) * np.exp(-np.linspace(0, 5, int(0.35 * SR))),
        sine(60, 0.35, 0.2) * np.exp(-np.linspace(0, 4, int(0.35 * SR))),
    )

    out["sentry_notice"] = stack(
        square(NOTE["A4"], 0.06, 0.4) * env(int(0.06 * SR), 0.002, 0.02),
        np.pad(square(NOTE["D5"], 0.08, 0.35) * env(int(0.08 * SR), 0.002, 0.03), (int(0.05 * SR), 0)),
    )
    out["sentry_windup"] = seq(
        square(NOTE["D3"], 0.12, 0.3) * env(int(0.12 * SR), 0.02, 0.02),
        square(NOTE["F3"], 0.12, 0.35) * env(int(0.12 * SR), 0.02, 0.02),
        square(NOTE["A3"], 0.15, 0.4) * env(int(0.15 * SR), 0.02, 0.05),
    )
    out["sentry_hit"] = stack(
        noise(0.1, 0.6) * np.exp(-np.linspace(0, 20, int(0.1 * SR))),
        square(NOTE["D2"], 0.12, 0.5) * env(int(0.12 * SR), 0.002, 0.06),
    )

    out["player_hurt"] = stack(
        square(NOTE["A3"], 0.08, 0.45) * env(int(0.08 * SR), 0.002, 0.04),
        noise(0.1, 0.35) * np.exp(-np.linspace(0, 22, int(0.1 * SR))),
    )
    out["player_downed"] = stack(
        triangle(NOTE["D3"], 0.35, 0.4) * env(int(0.35 * SR), 0.01, 0.2),
        sine(NOTE["A2"], 0.4, 0.25) * env(int(0.4 * SR), 0.01, 0.25),
    )
    out["player_revived"] = stack(
        music_box_pluck(NOTE["D4"], 0.3, 0.3),
        music_box_pluck(NOTE["A4"], 0.35, 0.35),
        music_box_pluck(NOTE["D5"], 0.4, 0.3),
    )
    out["player_died"] = stack(
        triangle(NOTE["A3"], 0.25, 0.35) * env(int(0.25 * SR), 0.01, 0.15),
        triangle(NOTE["D3"], 0.4, 0.35) * env(int(0.4 * SR), 0.02, 0.25),
        sine(NOTE["D2"], 0.5, 0.3) * env(int(0.5 * SR), 0.02, 0.3),
    )

    out["relic_lift"] = stack(
        music_box_pluck(NOTE["D5"], 0.5, 0.35),
        music_box_pluck(NOTE["A5"], 0.6, 0.4),
        glass_pad(NOTE["D4"], 1.2, 0.2),
        drone([NOTE["D3"], NOTE["A3"]], 1.0, 0.15),
    )
    out["escape"] = stack(
        music_box_pluck(NOTE["F4"], 0.4, 0.3),
        music_box_pluck(NOTE["A4"], 0.45, 0.35),
        music_box_pluck(NOTE["D5"], 0.7, 0.4),
        glass_pad(NOTE["D4"], 1.5, 0.18),
    )

    out["tier_rise"] = seq(
        square(NOTE["D3"], 0.1, 0.3) * env(int(0.1 * SR), 0.005, 0.04),
        square(NOTE["F3"], 0.1, 0.35) * env(int(0.1 * SR), 0.005, 0.04),
        square(NOTE["A3"], 0.12, 0.4) * env(int(0.12 * SR), 0.005, 0.05),
        square(NOTE["D4"], 0.2, 0.45) * env(int(0.2 * SR), 0.005, 0.1),
    )

    out["collapse_alarm"] = seq(
        *([
            square(NOTE["A4"], 0.15, 0.4) * env(int(0.15 * SR), 0.005, 0.05),
            square(NOTE["D4"], 0.15, 0.4) * env(int(0.15 * SR), 0.005, 0.05),
            np.zeros(int(0.08 * SR)),
        ] * 3)
    )
    out["collapse_tick"] = sfx_blip(NOTE["A5"], 0.04, 0.35, "tri")

    out["ui_hover"] = sfx_blip(NOTE["A4"], 0.035, 0.25, "tri")
    out["ui_click"] = sfx_blip(NOTE["D5"], 0.05, 0.4, "square")

    return {k: normalize(np.asarray(v, dtype=np.float64).reshape(-1), 0.9) for k, v in out.items()}


def make_loop(x: np.ndarray, overlap_sec: float) -> np.ndarray:
    """Seamless loop: crossfade the last `overlap_sec` into the first, then drop that tail.

    The end of the result flows straight into its start (no click, no gap). Use exactly one beat so the
    rhythm grid survives; every exploration layer uses the same overlap so they stay sample-aligned.
    """
    n = int(round(overlap_sec * SR))
    if n <= 0 or len(x) <= 2 * n:
        return x
    out = x[:-n].copy()
    t = np.linspace(0.0, 1.0, n)
    fade_in, fade_out = np.sin(t * np.pi / 2), np.cos(t * np.pi / 2)  # equal-power
    out[:n] = x[:n] * fade_in + x[-n:] * fade_out
    return out


# Music is normalized lower than SFX: Vorbis encoding overshoots peaks, and loops sit under the effects.
MUSIC_PEAK = 0.8


def save_music(name: str, samples: np.ndarray, loop_beat: float | None = None) -> None:
    if loop_beat is not None:
        samples = make_loop(samples, loop_beat)
    wav = RAW_OUT / "music" / f"{name}.wav"
    ogg = MUSIC_OUT / f"{name}.ogg"
    write_wav(wav, samples, peak=MUSIC_PEAK)
    wav_to_ogg(wav, ogg)
    print(f"  music/{name}.ogg  ({len(samples) / SR:.1f}s{', loop' if loop_beat else ''})")


def main() -> None:
    MUSIC_OUT.mkdir(parents=True, exist_ok=True)
    SFX_OUT.mkdir(parents=True, exist_ok=True)
    (RAW_OUT / "music").mkdir(parents=True, exist_ok=True)
    (RAW_OUT / "sfx").mkdir(parents=True, exist_ok=True)

    print("Generating music (D minor, 70 bpm layers)…")
    save_music("explore_calm", exploration_layer("calm"), loop_beat=beat(70))
    save_music("explore_restless", exploration_layer("restless"), loop_beat=beat(70))
    save_music("explore_hunting", exploration_layer("hunting"), loop_beat=beat(70))
    save_music("menu", menu_track(), loop_beat=beat(70))
    save_music("warden", warden_layer(), loop_beat=beat(70))
    save_music("collapse", collapse_track(), loop_beat=beat(140))
    save_music("victory", victory_sting())
    save_music("death", death_sting())

    print("Generating SFX…")
    for name, samples in make_sfx().items():
        # Pad card SFX that were summed with different lengths
        if isinstance(samples, np.ndarray) and samples.ndim > 1:
            samples = samples.flatten()
        # Fix card_* that used + with different lengths via pad
        path = SFX_OUT / f"{name}.wav"
        write_wav(path, samples)
        write_wav(RAW_OUT / "sfx" / f"{name}.wav", samples)
        print(f"  sfx/{name}.wav  ({len(samples) / SR:.2f}s)")

    print("Done.")


if __name__ == "__main__":
    main()
