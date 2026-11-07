import { useEffect, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router';
import { api, errText, nzDateTime } from '../api';
import { useSession } from '../App';

export interface ModuleRow {
  id: string; name: string; description: string; available: boolean; access: string; state: 'NotLicensed' | 'NeedsSetUp' | 'Active' | 'ReadOnly' | 'ComingLater';
  setUpAt: string | null; setUpBy: string | null; requiredStepsOpen: number | null; hasWizard: boolean; skippedChecks: string | null;
}
interface Info {
  installationCode: string; health: string; messages: string[]; developmentKey: boolean;
  licence: null | { licenceId: string; customer: string; site: string | null; edition: string; expiresOn: string; installationCode: string | null; maxUsers: number | null; graceDays: number; activation: null | { url: string; checkInDays: number } };
  installedAt: string | null; installedBy: string | null; activationCheckedAt: string | null; activationError: string | null;
}

export const stateLabel: Record<ModuleRow['state'], [string, string]> = {
  Active: ['Active', 'green'], NeedsSetUp: ['Needs set-up', 'amber'], ReadOnly: ['Read-only (licence problem)', 'red'], NotLicensed: ['Not licensed', ''], ComingLater: ['Coming later', ''],
};
const healthLabel: Record<string, [string, string]> = {
  Valid: ['Licence valid', 'green'], ExpiringSoon: ['Expiring soon', 'amber'], InGrace: ['Expired – grace period', 'amber'], Missing: ['No licence', 'red'],
  Expired: ['Expired', 'red'], Invalid: ['Invalid', 'red'], WrongInstallation: ['For a different server', 'red'], ActivationOverdue: ['Online check overdue', 'amber'], ClockProblem: ['Server clock problem', 'red'],
};

/** Licence & modules: load a licence in one click, see every module's state, and open each module's guided set-up. */
export default function Licence() {
  const { refresh } = useSession();
  const location = useLocation();
  const [info, setInfo] = useState<Info | null>(null);
  const [modules, setModules] = useState<ModuleRow[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>((location.state as { done?: string } | null)?.done ? { ok: true, text: (location.state as { done: string }).done } : null);
  const [paste, setPaste] = useState('');
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const resultRef = useRef<HTMLDivElement>(null);

  const load = async () => {
    try {
      const [i, m] = await Promise.all([api<Info>('/api/admin/licence'), api<ModuleRow[]>('/api/admin/modules')]);
      setInfo(i); setModules(m);
    } catch (e) { setError(errText(e)); }
  };
  useEffect(() => { load(); }, []);
  useEffect(() => { if (result) resultRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' }); }, [result]);

  const install = async (text: string, source: string) => {
    setBusy(true); setResult(null);
    try {
      await api('/api/admin/licence', { body: { text } });
      await load(); await refresh();
      setResult({ ok: true, text: `Licence installed from ${source}. Modules it includes are now unlocked – set each one up below.` });
      setPaste('');
    } catch (e) { setResult({ ok: false, text: `The licence was not installed: ${errText(e)}` }); }
    finally { setBusy(false); if (fileRef.current) fileRef.current.value = ''; }
  };
  const onFile = async (f: File | undefined) => { if (f) await install(await f.text(), `the file ${f.name}`); };
  const activate = async () => {
    setBusy(true); setResult(null);
    try { const r = await api<{ message: string }>('/api/admin/licence/activate', { body: {} }); setResult({ ok: true, text: r.message }); await load(); await refresh(); }
    catch (e) { setResult({ ok: false, text: errText(e) }); } finally { setBusy(false); }
  };

  if (!info || !modules) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const l = info.licence;
  const [hText, hClass] = healthLabel[info.health] ?? [info.health, 'amber'];
  const built = modules.filter((m) => m.available);
  const later = modules.filter((m) => !m.available);
  const needs = built.filter((m) => m.state === 'NeedsSetUp').length;

  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Licence &amp; modules</h1><p className="muted">Load the licence, then set up each module. Everything is already installed – the licence decides what can be used.</p></div>

      <section className="card action stack">
        <div className="row-between">
          <h2>Licence <span className={`pill ${hClass}`}>{hText}</span>{info.developmentKey && <span className="pill amber">Sandbox – not for real patients</span>}</h2>
          <div className="row">
            <input ref={fileRef} type="file" accept=".lic,.txt" className="sr-only" id="licfile" onChange={(e) => onFile(e.target.files?.[0])} />
            <button className="btn btn-primary btn-big" disabled={busy} onClick={() => fileRef.current?.click()}>{busy ? 'Installing…' : l ? 'Load a new licence file' : 'Load licence file'}</button>
            {l?.activation && <button className="btn btn-light" disabled={busy} onClick={activate}>Check licence online now</button>}
          </div>
        </div>
        <div ref={resultRef} aria-live="polite">{result && <p className={result.ok ? 'notice' : 'error'} role={result.ok ? 'status' : 'alert'}>{result.text}</p>}</div>
        {info.messages.length > 0 && <ul className="plain">{info.messages.map((m, i) => <li key={i}>{m}</li>)}</ul>}
        {l && (
          <dl className="facts">
            <dt>Customer</dt><dd>{l.customer}{l.site ? ` – ${l.site}` : ''} · {l.edition} · {l.licenceId}</dd>
            <dt>Valid until</dt><dd>{l.expiresOn} (+{l.graceDays} days grace)</dd>
            <dt>Locked to</dt><dd>{l.installationCode ?? 'Any server (sandbox/evaluation)'}</dd>
            <dt>Users</dt><dd>{l.maxUsers ?? 'Unlimited'}</dd>
            {l.activation && <><dt>Online check</dt><dd>every {l.activation.checkInDays} days · last {nzDateTime(info.activationCheckedAt)}</dd></>}
            <dt>Installed</dt><dd>{nzDateTime(info.installedAt)} by {info.installedBy}</dd>
          </dl>
        )}
        {info.activationError && <p className="error">{info.activationError}</p>}
        <details>
          <summary>Need a licence? This server's installation code</summary>
          <div className="stack mt">
            <p className="code-big">{info.installationCode}</p>
            <p className="hint">Send this code to the vendor to get a licence locked to this server. It changes if BrightPAS is moved to another server or database.</p>
            <div><button className="btn btn-light btn-small" onClick={() => navigator.clipboard?.writeText(info.installationCode)}>Copy code</button></div>
          </div>
        </details>
        <details>
          <summary>Paste the licence text instead</summary>
          <div className="stack mt">
            <textarea className="mono" value={paste} onChange={(e) => setPaste(e.target.value)} placeholder="-----BEGIN BRIGHTPAS LICENCE-----" aria-label="Licence text" />
            <div><button className="btn btn-primary" disabled={busy || !paste.trim()} onClick={() => install(paste, 'pasted text')}>Install pasted licence</button></div>
          </div>
        </details>
      </section>

      <section className="card stack">
        <div className="row-between">
          <h2>Modules</h2>
          {needs > 0 && <span className="pill amber">{needs} module{needs === 1 ? '' : 's'} to set up</span>}
        </div>
        <div className="module-grid">
          {built.map((m) => {
            const [t, c] = stateLabel[m.state];
            return (
              <div key={m.id} className={`module-card ${m.state}`}>
                <div className="row-between"><strong>{m.name}</strong><span className={`pill ${c}`}>{t}</span></div>
                <p className="small muted">{m.description}</p>
                {m.state === 'NeedsSetUp' && <p className="small">{m.requiredStepsOpen ? `${m.requiredStepsOpen} required step${m.requiredStepsOpen === 1 ? '' : 's'} left.` : 'All required steps done – finish set-up.'}</p>}
                {m.state === 'Active' && m.setUpAt && <p className="small muted">Set up {nzDateTime(m.setUpAt)} by {m.setUpBy}{m.requiredStepsOpen ? ` · ${m.requiredStepsOpen} step(s) now need attention` : ''}</p>}
                {m.state === 'NotLicensed' && <p className="small muted">Included in the installation. Ask the vendor to add it to the licence.</p>}
                {m.hasWizard && (m.state === 'NeedsSetUp' || m.state === 'Active') && (
                  <div><Link className={`btn btn-small ${m.state === 'NeedsSetUp' ? 'btn-primary' : 'btn-light'}`} to={`/admin/modules/${m.id}`}>{m.state === 'NeedsSetUp' ? 'Set up' : 'Review set-up'}</Link></div>
                )}
              </div>
            );
          })}
        </div>
        <details>
          <summary>Coming later ({later.length})</summary>
          <ul className="plain mt">{later.map((m) => <li key={m.id}><strong>{m.name}</strong> – <span className="muted">{m.description}</span></li>)}</ul>
        </details>
      </section>
    </div>
  );
}
