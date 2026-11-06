import { useState } from 'react';
import { api, ApiError } from '../api';
import TotpSetup, { RecoveryCodes } from '../components/TotpSetup';

const errorText: Record<string, string> = {
  InvalidCode: 'That set-up code is not correct. Check the code shown at the end of Setup.',
  CodeExpired: 'That set-up code has expired. Run Setup again to get a new one.',
  AlreadyCompleted: 'The first administrator has already been set up. Please sign in.',
  PasswordRejected: 'Please choose a different password.',
  InvalidMfaCode: 'That code is not correct. Try the newest code from your app.',
};

export default function FirstAdmin({ onDone }: { onDone: () => void }) {
  const [step, setStep] = useState(1);
  const [code, setCode] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [enrol, setEnrol] = useState<{ sharedKey: string; otpAuthUri: string } | null>(null);
  const [recovery, setRecovery] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  const fail = (e: unknown) => {
    const body = e instanceof ApiError ? e.body : null;
    setError(errorText[body?.error] ?? 'Something went wrong. Please try again.');
    setMessages(body?.messages ?? []);
  };

  const createAccount = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null); setMessages([]);
    if (password !== confirm) { setError('The two passwords do not match.'); return; }
    setBusy(true);
    try { setEnrol(await api('/api/setup/first-admin/start', { body: { code, userName, displayName, password } })); setStep(3); }
    catch (err) { fail(err); } finally { setBusy(false); }
  };

  const verify = async (totp: string) => {
    setBusy(true); setError(null);
    try { const r = await api<{ recoveryCodes: string[] }>('/api/setup/first-admin/complete', { body: { code, userName, totpCode: totp } }); setRecovery(r.recoveryCodes); setStep(4); }
    catch (err) { fail(err); } finally { setBusy(false); }
  };

  return (
    <div className="auth-page">
      <div className="auth-card wide">
        <div className="auth-brand"><span className="brand-mark">G</span><div><h1>Welcome to BrightPAS</h1><p className="muted small">First-time set-up</p></div></div>
        <ol className="progress" aria-label="Set-up progress">
          {['Set-up code', 'Your account', 'Phone app', 'Recovery codes'].map((s, i) => (
            <li key={s} className={step === i + 1 ? 'current' : step > i + 1 ? 'done' : ''}>{s}</li>
          ))}
        </ol>
        {step === 1 && (
          <form className="stack" onSubmit={(e) => { e.preventDefault(); setStep(2); }}>
            <p>Enter the one-time code that was shown at the end of the BrightPAS Setup program on the server.</p>
            <input className="code-input" aria-label="Set-up code" placeholder="XXXX-XXXX-XXXX-XXXX" autoFocus value={code}
              onChange={(e) => setCode(e.target.value.toUpperCase())} required />
            <button className="btn btn-primary btn-big" disabled={code.replace(/[^A-Z0-9]/g, '').length !== 16}>Next</button>
          </form>
        )}
        {step === 2 && (
          <form className="stack" onSubmit={createAccount}>
            <p>Create the first <strong>System Administrator</strong> account. This account manages users and settings; it does not give access to patient records.</p>
            <label>Your full name<input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required autoFocus /></label>
            <label>Username<input value={userName} onChange={(e) => setUserName(e.target.value.trim())} autoComplete="username" required /></label>
            <label>Password<input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required minLength={12} /></label>
            <p className="hint">At least 12 characters. A short sentence works well, e.g. “four kiwi walk to Nelson”. Avoid common words and the organisation name.</p>
            <label>Type the password again<input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" required /></label>
            {error && <p className="error" role="alert">{error}</p>}
            {messages.length > 0 && <ul className="error">{messages.map((m) => <li key={m}>{m}</li>)}</ul>}
            <div className="row">
              <button type="button" className="btn btn-light" onClick={() => setStep(1)}>Back</button>
              <button className="btn btn-primary btn-big" disabled={busy}>{busy ? 'Creating…' : 'Create account'}</button>
            </div>
          </form>
        )}
        {step === 3 && enrol && <TotpSetup {...enrol} busy={busy} error={error} onSubmit={verify} />}
        {step === 4 && <RecoveryCodes codes={recovery} onContinue={onDone} />}
      </div>
    </div>
  );
}
