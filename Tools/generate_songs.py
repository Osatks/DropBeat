"""Generates the game's songs with the ElevenLabs Music API (reads ELEVENLABS_API_KEY from the environment).

Usage: python Tools/generate_songs.py [--length 80] [--only "Name,Name"]
Each song is described in Tools/songs.json (name, bpm, key, style). The prompt asks for an instrumental
with a steady tempo and clear drums, which the beat analyzer needs. Files land in Assets/DropBeat/Audio
(written to a temp file first so Unity never imports a half-written mp3). Existing files are skipped,
so re-running only generates what is missing. Two requests run at once (the Starter plan's limit).
"""
import argparse, json, os, sys, tempfile, time, urllib.error, urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'Assets', 'DropBeat', 'Audio')
URL = 'https://api.elevenlabs.io/v1/music?output_format=mp3_44100_128'


def prompt_for(song):
    return (f"Instrumental {song['style']}. {song['bpm']} BPM, {song['key']}. "
            "Steady, constant tempo from start to end with no tempo changes and no rubato. "
            "Clear, punchy drums throughout: a strong kick drum on the beat and a snare or clap on beats 2 and 4. "
            "Short intro, energetic build, a big drop, then an outro. Clean modern mix. No vocals.")


def generate(song, length_s, key):
    dest = os.path.join(OUT, song['name'] + '.mp3')
    if os.path.exists(dest):
        return song['name'], 'exists'
    body = json.dumps({
        'prompt': prompt_for(song),
        'music_length_ms': int(length_s * 1000),
        'model_id': 'music_v2_5',
        'force_instrumental': True,
    }).encode('utf-8')
    data = None
    for attempt in range(4):
        req = urllib.request.Request(URL, data=body, method='POST', headers={
            'xi-api-key': key, 'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                data = r.read()
            break
        except urllib.error.HTTPError as e:
            detail = e.read().decode('utf-8', 'replace')[:400]
            if e.code == 429 and attempt < 3:  # rate limited: nothing was generated, safe to retry
                time.sleep(20 * (attempt + 1))
                continue
            return song['name'], f'HTTP {e.code}: {detail}'
        except Exception as e:  # URLError, timeouts, IncompleteRead, SSL errors...
            # The request may still have been charged; never resend automatically.
            return song['name'], f'network error, not retried (check credits before re-running): {e}'
    if data is None:
        return song['name'], 'rate limited, gave up'

    # Paid audio is in memory now: save it next to the destination (same drive) under a hidden name
    # Unity ignores, then rename. Any error here is reported, never retried as a new request.
    tmp = os.path.join(OUT, '.' + song['name'] + '.mp3.part')
    try:
        with open(tmp, 'wb') as f:
            f.write(data)
        os.replace(tmp, dest)
    except OSError as e:
        backup = os.path.join(tempfile.gettempdir(), song['name'] + '.mp3')
        try:
            with open(backup, 'wb') as f:
                f.write(data)
        except OSError as e2:
            return song['name'], f'PAID AUDIO NOT SAVED ({e}; backup failed: {e2})'
        return song['name'], f'saved to {backup} instead ({e})'
    finally:
        if os.path.exists(tmp):
            try:
                os.remove(tmp)
            except OSError:
                pass
    return song['name'], f'ok {len(data) // 1024} KB'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--length', type=float, default=80)
    ap.add_argument('--only', default='')
    args = ap.parse_args()
    key = os.environ.get('ELEVENLABS_API_KEY')
    if not key:
        sys.exit('ELEVENLABS_API_KEY is not set')
    songs = json.load(open(os.path.join(ROOT, 'Tools', 'songs.json'), encoding='utf-8'))
    if args.only:
        wanted = {s.strip() for s in args.only.split(',')}
        songs = [s for s in songs if s['name'] in wanted]
    os.makedirs(OUT, exist_ok=True)
    with ThreadPoolExecutor(max_workers=2) as pool:
        for name, result in pool.map(lambda s: generate(s, args.length, key), songs):
            print(f'{name}: {result}', flush=True)


if __name__ == '__main__':
    main()
