import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api, errText } from '../api';
import { useSession } from '../App';
import { ModuleRow, stateLabel } from './Licence';

interface Check {
  key: string; title: string; explanation: string; ok: boolean; status: string; optional: boolean;
  actionLabel: string | null; actionLink: string | null; actionPermission: string | null; whoCanDoIt: string | null;
}

/** Guided set-up for one module: each step is checked live against the system, with a button to the screen where it is done. */
export default function ModuleSetup() {
  const { id } = useParams();
  const { can, refresh } = useSession();
  const navigate = useNavigate();
  const [data, setData] = useState<{ module: ModuleRow; checks: Check[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    setError(null);
    try { setData(await api(`/api/admin/modules/${id}/setup`)); } catch (e) { setError(errText(e)); }
  };
  useEffect(() => { load(); }, [id]);

  const finish = async () => {
    setBusy(true); setError(null);
    try {
      await api(`/api/admin/modules/${id}/setup/complete`, { body: {} });
      await refresh();
      navigate('/admin/licence', { state: { done: `${data!.module.name} is set up and now shows in the menu for the staff who use it.` } });
    } catch (e) { setError(errText(e)); await load(); } finally { setBusy(false); }
  };
  const reopen = async () => {
    setBusy(true);
    try { await api(`/api/admin/modules/${id}/setup/reopen`, { body: {} }); await refresh(); await load(); } catch (e) { setError(errText(e)); } finally { setBusy(false); }
  };

  if (!data) return error ? <div className="stack"><p className="error">{error}</p><Link to="/admin/licence">Back to Licence &amp; modules</Link></div> : <p>Loading…</p>;
  const { module: m, checks } = data;
  const required = checks.filter((c) => !c.optional);
  const doneRequired = required.filter((c) => c.ok).length;
  const ready = doneRequired === required.length;
  const [stText, stClass] = stateLabel[m.state];
  const firstOpen = checks.find((c) => !c.ok && !c.optional) ?? null;

  return (
    <div className="stack-lg">
      <div className="page-head">
        <p className="small"><Link to="/admin/licence">← Licence &amp; modules</Link></p>
        <h1>Set up: {m.name} <span className={`pill ${stClass}`}>{stText}</span></h1>
        <p className="muted">{m.description}</p>
      </div>
      {m.state === 'NotLicensed' && <p className="warn-box">This module is not licensed. Load a licence that includes it first.</p>}

      <section className="card stack">
        <div className="row-between">
          <h2>{ready ? 'All required steps are done' : `Step ${doneRequired + 1} of ${required.length}`}</h2>
          <button className="btn btn-light btn-small" onClick={load}>Check again</button>
        </div>
        <div className="progressbar" role="progressbar" aria-label="Set-up steps done" aria-valuemin={0} aria-valuemax={required.length} aria-valuenow={doneRequired}>
          <span className={`fill w${Math.round((doneRequired / Math.max(required.length, 1)) * 10)}`} />
        </div>
        <ol className="setup-steps">
          {checks.map((c) => (
            <li key={c.key} className={`${c.ok ? 'ok' : c.optional ? 'optional' : 'todo'} ${firstOpen?.key === c.key ? 'current' : ''}`}>
              <span className="mark" aria-hidden="true">{c.ok ? '✓' : c.optional ? '–' : '○'}</span>
              <div className="stack-sm">
                <div><strong>{c.title}</strong>{c.optional && <span className="badge">Optional</span>}</div>
                <p className="small muted">{c.explanation}</p>
                <p className={`small ${c.ok ? 'ok-text' : c.optional ? 'muted' : 'warn-text'}`}>{c.status}</p>
                {!c.ok && c.actionLink && (c.actionPermission === null || can(c.actionPermission)
                  ? <div><Link className={`btn btn-small ${firstOpen?.key === c.key ? 'btn-primary' : 'btn-light'}`} to={c.actionLink}>{c.actionLabel}</Link></div>
                  : <p className="small">Done by: <strong>{c.whoCanDoIt ?? 'a user with the right permission'}</strong>. Ask them to do this, then press “Check again”.</p>)}
                {!c.ok && !c.actionLink && c.whoCanDoIt && <p className="small">Done by: <strong>{c.whoCanDoIt}</strong>.</p>}
              </div>
            </li>
          ))}
        </ol>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="row">
          {m.state !== 'Active'
            ? <button className="btn btn-primary btn-big" disabled={!ready || busy || m.state === 'NotLicensed'} onClick={finish}>{busy ? 'Saving…' : 'Finish set-up'}</button>
            : <button className="btn btn-light" disabled={busy} onClick={reopen}>Mark as needing set-up again</button>}
          {!ready && <p className="hint">Finish the required steps first. Optional steps can be done later.</p>}
        </div>
      </section>
    </div>
  );
}
