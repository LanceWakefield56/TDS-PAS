import { useEffect, useState } from 'react';
import { api, errText, nzDateTime } from '../api';
import { useSession } from '../App';

interface Mod { id: string; name: string; description: string; available: boolean; access: string }
interface Info {
  installationCode: string; health: string; messages: string[]; developmentKey: boolean; productionKeyBuiltIn: boolean;
  licence: null | { licenceId: string; customer: string; site: string | null; edition: string; issuedAt: string; expiresOn: string; installationCode: string | null; maxUsers: number | null; graceDays: number; activation: null | { url: string; checkInDays: number; offlineGraceDays: number } };
  installedAt: string | null; installedBy: string | null; activationCheckedAt: string | null; activationError: string | null; modules: Mod[];
}
const accessLabel: Record<string, [string, string]> = { Full: ['Licensed', 'green'], ReadOnly: ['Read-only', 'amber'], None: ['Not licensed', ''] };

export default function Licence() {
  const { refresh } = useSession();
  const [info, setInfo] = useState<Info | null>(null);
  const [text, setText] = useState('');
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = async () => { try { setInfo(await api<Info>('/api/admin/licence')); } catch (e) { setError(errText(e)); } };
  useEffect(() => { load(); }, []);

  const install = async () => {
    setError(null); setMsg(null);
    try { await api('/api/admin/licence', { body: { text } }); setText(''); setMsg('Licence installed.'); await load(); await refresh(); } catch (e) { setError(errText(e)); }
  };
  const activate = async () => {
    setError(null); setMsg(null);
    try { const r = await api<{ message: string }>('/api/admin/licence/activate', { body: {} }); setMsg(r.message); await load(); await refresh(); } catch (e) { setError(errText(e)); }
  };
  const readFile = async (f: File | undefined) => { if (f) setText(await f.text()); };

  if (!info) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const l = info.licence;
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Licence &amp; modules</h1><p className="muted">Which parts of BrightPAS this server is licensed to use.</p></div>
      {msg && <p className="notice" role="status">{msg}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="grid">
        <section className="card action">
          <h2>This server's installation code</h2>
          <p className="code-big">{info.installationCode}</p>
          <p className="hint">Send this code to the vendor to get a licence locked to this server. It changes if BrightPAS is moved to another server or database.</p>
          <button className="btn btn-light btn-small" onClick={() => navigator.clipboard?.writeText(info.installationCode)}>Copy code</button>
        </section>
        <section className="card">
          <h2>Status <span className={`pill ${info.health === 'Valid' ? 'green' : info.health === 'Missing' ? '' : 'amber'}`}>{info.health}</span></h2>
          <ul className="plain">{info.messages.map((m, i) => <li key={i}>{m}</li>)}</ul>
          {l && (
            <dl className="facts mt">
              <dt>Licence</dt><dd>{l.licenceId}</dd>
              <dt>Customer</dt><dd>{l.customer}{l.site ? ` – ${l.site}` : ''}</dd>
              <dt>Edition</dt><dd>{l.edition}{info.developmentKey &&  <span className="pill amber">Development key</span>}</dd>
              <dt>Valid until</dt><dd>{l.expiresOn} (+{l.graceDays} days grace)</dd>
              <dt>Locked to</dt><dd>{l.installationCode ?? 'Any server (sandbox/evaluation)'}</dd>
              <dt>Users</dt><dd>{l.maxUsers ?? 'Unlimited'}</dd>
              <dt>Online check</dt><dd>{l.activation ? `${l.activation.url} every ${l.activation.checkInDays} days · last ${nzDateTime(info.activationCheckedAt)}` : 'Not used (offline licence)'}</dd>
              <dt>Installed</dt><dd>{nzDateTime(info.installedAt)} by {info.installedBy}</dd>
            </dl>
          )}
          {info.activationError && <p className="error">{info.activationError}</p>}
          {l?.activation && <button className="btn btn-light" onClick={activate}>Check licence online now</button>}
        </section>
      </div>
      <section className="card">
        <h2>Modules</h2>
        <table className="results compact">
          <thead><tr><th>Module</th><th>What it does</th><th>Status</th></tr></thead>
          <tbody>{info.modules.map((m) => (
            <tr key={m.id}>
              <td data-label="Module"><strong>{m.name}</strong></td>
              <td data-label="What it does">{m.description}{!m.available && <span className="muted"> (coming later)</span>}</td>
              <td data-label="Status"><span className={`pill ${accessLabel[m.access][1]}`}>{accessLabel[m.access][0]}</span></td>
            </tr>
          ))}</tbody>
        </table>
      </section>
      <section className="card">
        <h2>Install a licence</h2>
        <p className="hint">Open the licence file (.lic) you received, or paste its contents. Any change to the file makes it invalid.</p>
        <div className="stack">
          <label>Licence file<input type="file" accept=".lic,.txt" onChange={(e) => readFile(e.target.files?.[0])} /></label>
          <label>Or paste the licence<textarea className="mono" value={text} onChange={(e) => setText(e.target.value)} placeholder="-----BEGIN BRIGHTPAS LICENCE-----" /></label>
          <div><button className="btn btn-primary" disabled={!text.trim()} onClick={install}>Install licence</button></div>
        </div>
      </section>
    </div>
  );
}
