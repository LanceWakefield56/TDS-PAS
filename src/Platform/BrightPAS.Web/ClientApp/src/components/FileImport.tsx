import { useState } from 'react';

/** Result of one imported file, as returned by the import endpoints (0.18.0). */
interface ImportItem { name: string; outcome: string; detail?: string | null }
interface ImportSummary { messages: number; accepted: number; duplicates: number; rejected: number; items: ImportItem[] }

/**
 * Offline import (0.18.0): when a device or interface connection is down, staff choose one or more files – from a USB stick,
 * a network share or a file the system saved – and each is sent in turn. Shows what happened to every message in every file.
 */
export default function FileImport({ url, title, hint, accept, onDone }: { url: string; title: string; hint: string; accept?: string; onDone?: () => void }) {
  const [busy, setBusy] = useState('');
  const [items, setItems] = useState<ImportItem[] | null>(null);
  const [totals, setTotals] = useState<{ accepted: number; duplicates: number; rejected: number } | null>(null);
  const [error, setError] = useState('');

  const send = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    setError(''); setItems(null); setTotals(null);
    const all: ImportItem[] = []; const t = { accepted: 0, duplicates: 0, rejected: 0 };
    for (const [n, f] of [...files].entries()) {
      setBusy(`Importing ${f.name} (${n + 1} of ${files.length})…`);
      try {
        const res = await fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'X-File-Name': encodeURIComponent(f.name).replace(/%20/g, ' '), 'Content-Type': 'application/octet-stream' }, body: f });
        const body = await res.json().catch(() => null);
        if (!res.ok) { all.push({ name: f.name, outcome: 'Rejected', detail: body?.errors?.join(' ') ?? `The server answered ${res.status}.` }); t.rejected++; continue; }
        const s = body as ImportSummary;
        if (s.items) all.push(...s.items); else all.push({ name: f.name, outcome: 'Accepted' });
        t.accepted += s.accepted ?? 1; t.duplicates += s.duplicates ?? 0; t.rejected += s.rejected ?? 0;
      } catch { all.push({ name: f.name, outcome: 'Failed', detail: 'Could not reach BrightPAS – check the network and try again.' }); t.rejected++; }
    }
    setBusy(''); setItems(all); setTotals(t);
    onDone?.();
  };

  return (
    <section className="card stack file-import">
      <h3>{title}</h3>
      <p className="hint">{hint}</p>
      <label className="btn btn-light file-pick">Choose file(s)…
        <input type="file" multiple accept={accept} className="sr-only" disabled={!!busy} onChange={(e) => { void send(e.target.files); e.target.value = ''; }} />
      </label>
      {busy && <p role="status">{busy}</p>}
      {error && <div className="error">{error}</div>}
      {totals && <p role="status"><strong>{totals.accepted}</strong> accepted · <strong>{totals.duplicates}</strong> already held · <strong>{totals.rejected}</strong> not imported</p>}
      {items && items.length > 0 && (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Import results (scrolls sideways)">
          <table className="results compact">
            <thead><tr><th>File / message</th><th>Result</th><th>Detail</th></tr></thead>
            <tbody>{items.map((i, n) => (
              <tr key={n}><td className="small">{i.name}</td><td><span className={`badge ${i.outcome === 'Accepted' ? 'ok' : i.outcome === 'Duplicate' ? '' : 'warn'}`}>{i.outcome === 'Duplicate' ? 'Already held' : i.outcome === 'Accepted' ? 'Accepted' : 'Not imported'}</span></td><td className="small">{i.detail ?? ''}</td></tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </section>
  );
}
