import { useEffect, useRef, useState } from 'react';
import { api, Perm } from '../api';
import { useSession } from '../App';

let statusCache: Promise<{ dictation: boolean }> | null = null;
const loadStatus = () => (statusCache ??= api<{ dictation: boolean }>('/api/scribe/status').catch(() => ({ dictation: false })).then((s) => { if (!s.dictation) statusCache = null; return s; }));

/**
 * Microphone button for a free-text field (BrightPAS Scribe dictation): press to talk, press again to finish.
 * The text comes back with voice commands applied ("new paragraph", "full stop", "scratch that"), the TDS dictionary and NZ spelling,
 * and warnings for doses, units, look-alike medicines and laterality. "Next field" moves to the next field.
 */
export default function DictateButton({ onText, label = 'Dictate' }: { onText: (text: string) => void; label?: string }) {
  const { mod, can } = useSession();
  const [ok, setOk] = useState(false);
  const [rec, setRec] = useState(false);
  const [busy, setBusy] = useState(false);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const media = useRef<{ r: MediaRecorder; parts: Blob[]; stream: MediaStream } | null>(null);
  const btn = useRef<HTMLButtonElement>(null);
  useEffect(() => { if (mod('scribe') && can(Perm.ScribeUse)) loadStatus().then((s) => setOk(s.dictation)); }, [mod, can]);
  useEffect(() => () => media.current?.stream.getTracks().forEach((t) => t.stop()), []);
  if (!ok) return null;

  const start = async () => {
    setError(null); setWarnings([]);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
      const type = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4', 'audio/webm'].find((m) => MediaRecorder.isTypeSupported(m)) ?? '';
      const r = new MediaRecorder(stream, type ? { mimeType: type } : undefined);
      const parts: Blob[] = [];
      r.ondataavailable = (e) => { if (e.data.size) parts.push(e.data); };
      r.start(1000);
      media.current = { r, parts, stream }; setRec(true);
    } catch { setError('The microphone could not be opened – check it is plugged in and allowed for this site.'); }
  };
  const stop = async () => {
    const m = media.current; if (!m) return;
    media.current = null; setRec(false); setBusy(true);
    await new Promise<void>((res) => { m.r.onstop = () => res(); m.r.stop(); });
    m.stream.getTracks().forEach((t) => t.stop());
    try {
      const blob = new Blob(m.parts, { type: m.r.mimeType || 'audio/webm' });
      const res = await fetch('/api/scribe/dictate', { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'Content-Type': blob.type }, body: blob });
      const j = await res.json().catch(() => null);
      if (!res.ok) throw new Error(j?.errors?.join(' ') ?? `HTTP ${res.status}`);
      if (j.text) onText(j.text);
      setWarnings([...new Set<string>((j.highlights ?? []).map((h: any) => h.message))]);
      if ((j.commands ?? []).includes('next field')) {
        const fields = Array.from(document.querySelectorAll<HTMLElement>('textarea, input[type=text], input:not([type])'));
        const here = btn.current?.closest('label')?.querySelector<HTMLElement>('textarea, input');
        const i = here ? fields.indexOf(here) : -1;
        fields[i + 1]?.focus();
      }
    } catch (e: any) { setError('Dictation failed: ' + e.message); } finally { setBusy(false); }
  };
  return (
    <span className="dictate">
      <button ref={btn} type="button" className={rec ? 'btn btn-warn btn-small' : 'btn btn-light btn-small'} aria-pressed={rec} disabled={busy} aria-label={busy ? 'Typing dictation' : rec ? 'Stop dictating' : label}
        onClick={() => (rec ? stop() : start())}>{busy ? 'Typing…' : rec ? '● Stop dictating' : label}</button>
      {error && <span className="error-text small" role="alert"> {error}</span>}
      {warnings.length > 0 && <span className="small dictate-warn" role="status"> Check: {warnings.join(' ')}</span>}
    </span>
  );
}
