import { useEffect, useState } from 'react';
import { api, downloadPost, errText } from '../api';

let policyCache: { required: boolean; minLength: number } | null = null;

/**
 * Every export goes through this: by default an AES-256 encrypted, password-protected ZIP (opens with 7-Zip, WinZip or WinRAR).
 * The administrator decides whether protection is required. The password is never stored – tell the recipient by phone or a separate message.
 */
export type ExportFormat = 'pdf' | 'zip';
export default function ExportButton({ label, path, body, className = 'btn btn-light', disabled, formats = ['zip'] }: { label: string; path: string; body: Record<string, unknown>; className?: string; disabled?: boolean; formats?: ExportFormat[] }) {
  const [open, setOpen] = useState(false);
  const [format, setFormat] = useState<ExportFormat>(formats[0]);
  const [policy, setPolicy] = useState(policyCache);
  const [protect, setProtect] = useState(true);
  const [pw, setPw] = useState('');
  const [pw2, setPw2] = useState('');
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (open && !policy) api('/api/export-policy').then((p) => { policyCache = p; setPolicy(p); }).catch(() => setPolicy({ required: true, minLength: 10 })); }, [open, policy]);
  const go = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null); setMsg(null);
    const mustProtect = policy?.required || protect;
    if (mustProtect && pw !== pw2) { setError('The passwords do not match.'); return; }
    setBusy(true);
    try {
      const name = await downloadPost(path, { ...body, export: { protect: mustProtect, password: mustProtect ? pw : null, format } });
      setMsg(`Saved ${name}.${mustProtect ? ' Give the password to the recipient separately (phone, not the same email).' : ''}`);
      setPw(''); setPw2('');
    } catch (x) { setError(errText(x)); } finally { setBusy(false); }
  };
  if (!open) return <button type="button" className={className} disabled={disabled} onClick={() => setOpen(true)}>{label}</button>;
  return (
    <form className="card subtle stack export-box" onSubmit={go}>
      <strong>{label}</strong>
      {formats.length > 1 && <div className="row" role="radiogroup" aria-label="Format">
        {formats.map((f) => <label key={f} className="check"><input type="radio" name="export-format" checked={format === f} onChange={() => setFormat(f)} /> {f === 'pdf' ? 'PDF' : 'Encrypted ZIP'}</label>)}
      </div>}
      {!policy ? <p>Loading…</p> : <>
        {!policy.required && <label className="check"><input type="checkbox" checked={protect} onChange={(e) => setProtect(e.target.checked)} /> Password-protect (recommended – contains patient or staff information)</label>}
        {(policy.required || protect) && <div className="row">
          <label>Password (at least {policy.minLength} characters)<input type="password" autoComplete="new-password" value={pw} onChange={(e) => setPw(e.target.value)} required minLength={policy.minLength} /></label>
          <label>Type it again<input type="password" autoComplete="new-password" value={pw2} onChange={(e) => setPw2(e.target.value)} required /></label>
        </div>}
        <p className="hint">{(policy.required || protect) && format === 'pdf' ? 'You will get an AES-256 password-protected PDF. It opens in any PDF reader (Adobe, Edge, Chrome) with the password.' : policy.required || protect ? <>You will get an AES-256 encrypted ZIP. Open it with <a href="https://www.7-zip.org/download.html" target="_blank" rel="noopener noreferrer">7-Zip</a> (free), WinZip or WinRAR and the password.</> : 'The file will not be protected. This is recorded in the audit log.'}</p>
      </>}
      {error && <p className="error" role="alert">{error}</p>}
      {msg && <p className="ok-text">{msg}</p>}
      <div className="row"><button className="btn btn-primary" disabled={busy || !policy}>{busy ? 'Preparing…' : 'Download'}</button><button type="button" className="btn btn-light" onClick={() => setOpen(false)}>Close</button></div>
    </form>
  );
}
