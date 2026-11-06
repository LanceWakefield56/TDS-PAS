import { useEffect, useState } from 'react';
import QRCode from 'qrcode';

/** Shows the authenticator QR code and manual key, and collects the first 6-digit code. */
export default function TotpSetup({ otpAuthUri, sharedKey, busy, error, onSubmit }:
  { otpAuthUri: string; sharedKey: string; busy: boolean; error: string | null; onSubmit: (code: string) => void }) {
  const [qr, setQr] = useState<string | null>(null);
  const [code, setCode] = useState('');
  useEffect(() => { QRCode.toDataURL(otpAuthUri, { margin: 1, width: 220 }).then(setQr); }, [otpAuthUri]);
  return (
    <form className="stack" onSubmit={(e) => { e.preventDefault(); onSubmit(code); }}>
      <ol className="steps-list">
        <li>Open an authenticator app on your phone (Microsoft Authenticator, Google Authenticator or similar).</li>
        <li>Scan this code:</li>
      </ol>
      <div className="qr">{qr ? <img src={qr} alt="QR code for your authenticator app" width={220} height={220} /> : 'Preparing…'}</div>
      <details><summary>Can't scan? Type this key instead</summary><code className="key">{sharedKey}</code></details>
      <label>3. Enter the 6-digit code shown in the app
        <input className="code-input" inputMode="numeric" autoComplete="one-time-code" maxLength={7} autoFocus
          value={code} onChange={(e) => setCode(e.target.value.replace(/[^0-9 ]/g, ''))} required />
      </label>
      {error && <p className="error" role="alert">{error}</p>}
      <button className="btn btn-primary btn-big" disabled={busy || code.replace(/\s/g, '').length !== 6}>{busy ? 'Checking…' : 'Confirm'}</button>
    </form>
  );
}

export function RecoveryCodes({ codes, onContinue }: { codes: string[]; onContinue: () => void }) {
  const [saved, setSaved] = useState(false);
  return (
    <div className="stack">
      <p>Keep these <strong>recovery codes</strong> somewhere safe (for example, printed and locked away). Each one can be used once if you lose your phone.</p>
      <ul className="codes">{codes.map((c) => <li key={c}><code>{c}</code></li>)}</ul>
      <button className="btn btn-light" type="button" onClick={() => window.print()}>Print codes</button>
      <label className="check"><input type="checkbox" checked={saved} onChange={(e) => setSaved(e.target.checked)} /> I have saved my recovery codes</label>
      <button className="btn btn-primary btn-big" disabled={!saved} onClick={onContinue}>Continue</button>
    </div>
  );
}
