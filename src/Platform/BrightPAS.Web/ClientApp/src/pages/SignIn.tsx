import { useEffect, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router';
import { api } from '../api';
import { useSession } from '../App';
import TotpSetup, { RecoveryCodes } from '../components/TotpSetup';

type Stage = 'password' | 'mfa' | 'enrol' | 'codes';

export default function SignIn() {
  const { refresh } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const [stage, setStage] = useState<Stage>('password');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [enrol, setEnrol] = useState<{ sharedKey: string; otpAuthUri: string } | null>(null);
  const [recovery, setRecovery] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(params.get('expired') ? 'You were signed out. Please sign in again.' : null);
  const [busy, setBusy] = useState(false);

  // Demonstration systems only: the shared demo sign-in details (never shown on a real installation).
  const [demo, setDemo] = useState<DemoInfo | null>(null);
  useEffect(() => { api<{ demo: DemoInfo | null }>('/api/setup/status').then((s) => setDemo(s.demo)).catch(() => { /* ignore */ }); }, []);

  const finish = async () => { await refresh(); navigate((location.state as any)?.from ?? '/', { replace: true }); };

  const submitPassword = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setError(null);
    try {
      const r = await api<{ next: string }>('/api/auth/login', { body: { userName, password } });
      setPassword('');
      if (r.next === 'mfa') setStage('mfa');
      else if (r.next === 'enrol') { setEnrol(await api('/api/auth/enrol/begin', { body: {} })); setStage('enrol'); }
      else if (r.next === 'locked') setError('This account is locked for 15 minutes after too many attempts. Ask your manager if you need help.');
      else setError('Username or password is not correct.');
    } catch { setError('Could not sign in. Please try again.'); } finally { setBusy(false); }
  };

  const submitCode = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setError(null);
    try {
      const r = await api<{ next: string }>('/api/auth/mfa', { body: { code } });
      if (r.next === 'done') await finish();
      else if (r.next === 'locked') { setStage('password'); setError('Too many wrong codes – the account is locked for 15 minutes.'); }
      else { setCode(''); setError('That code is not correct. Wait for a new code and try again.'); }
    } catch { setStage('password'); setError('Your sign-in timed out. Please start again.'); } finally { setBusy(false); }
  };

  const submitEnrol = async (c: string) => {
    setBusy(true); setError(null);
    try {
      const r = await api<{ next: string; recoveryCodes?: string[] }>('/api/auth/enrol/complete', { body: { code: c } });
      if (r.next === 'done') { setRecovery(r.recoveryCodes ?? []); setStage('codes'); }
      else setError('That code is not correct. Check the time on your phone and try the newest code.');
    } catch { setStage('password'); setError('Your set-up timed out. Please sign in again.'); } finally { setBusy(false); }
  };

  return (
    <div className="auth-page">
      <div className="auth-card">
        <div className="auth-brand"><span className="brand-mark">G</span><div><h1>BrightPAS</h1><p className="muted small">Tasman Day Surgery · Patient administration</p></div></div>
        {stage === 'password' && (
          <form className="stack" onSubmit={submitPassword}>
            <label>Username<input autoComplete="username" autoFocus value={userName} onChange={(e) => setUserName(e.target.value)} required /></label>
            <label>Password<input type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required /></label>
            {error && <p className="error" role="alert">{error}</p>}
            <button className="btn btn-primary btn-big" disabled={busy}>{busy ? 'Signing in…' : 'Next'}</button>
            {demo && <DemoPanel demo={demo} pick={setUserName} />}
          </form>
        )}
        {stage === 'mfa' && (
          <form className="stack" onSubmit={submitCode}>
            <p>Enter the 6-digit code from your authenticator app.</p>
            <input className="code-input" inputMode="numeric" autoComplete="one-time-code" autoFocus aria-label="Authenticator code"
              value={code} onChange={(e) => setCode(e.target.value)} required />
            <p className="hint">Lost your phone? Type one of your recovery codes instead.</p>
            {error && <p className="error" role="alert">{error}</p>}
            <button className="btn btn-primary btn-big" disabled={busy}>{busy ? 'Checking…' : 'Sign in'}</button>
            <button type="button" className="btn btn-link" onClick={() => { setStage('password'); setCode(''); setError(null); }}>Back</button>
          </form>
        )}
        {stage === 'enrol' && enrol && (
          <>
            <h2>Set up two-step sign-in</h2>
            <p>For patient privacy, every account needs a code from your phone as well as a password.</p>
            <TotpSetup {...enrol} busy={busy} error={error} onSubmit={submitEnrol} />
          </>
        )}
        {stage === 'codes' && (<><h2>You're set up</h2><RecoveryCodes codes={recovery} onContinue={finish} /></>)}
      </div>
      <p className="auth-footer">Authorised staff only. All access is recorded.</p>
    </div>
  );
}

interface DemoInfo { state: string; password: string | null; authenticatorSecret: string | null; accounts: { userName: string; displayName: string; purpose: string }[] | null }

function DemoPanel({ demo, pick }: { demo: DemoInfo; pick: (u: string) => void }) {
  if (demo.state !== 'done') return <div className="demo-panel"><strong>Demonstration system</strong><p className="small">Demonstration data is {demo.state === 'failed' ? 'NOT loaded – see the website log' : 'being loaded – try again in a minute'}.</p></div>;
  return (
    <details className="demo-panel">
      <summary><strong>Demonstration system</strong> – sign-in details</summary>
      <p className="small">Every account uses the password <code>{demo.password}</code>. Add this key once to your authenticator app (it is the same for every demo account): <code>{demo.authenticatorSecret}</code></p>
      <table className="small"><tbody>
        {demo.accounts?.map((a) => (
          <tr key={a.userName}><td><button type="button" className="btn btn-link" onClick={() => pick(a.userName)}>{a.userName}</button></td><td>{a.displayName}<div className="muted">{a.purpose}</div></td></tr>
        ))}
      </tbody></table>
    </details>
  );
}
