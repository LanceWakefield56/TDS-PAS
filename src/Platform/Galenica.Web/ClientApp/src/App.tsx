import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { Navigate, Route, Routes, useLocation, useNavigate } from 'react-router';
import { api, Me } from './api';
import Shell from './components/Shell';
import SignIn from './pages/SignIn';
import FirstAdmin from './pages/FirstAdmin';
import Home from './pages/Home';
import PatientSearch from './pages/PatientSearch';
import PatientRegister from './pages/PatientRegister';
import PatientView from './pages/PatientView';
import AdminUsers from './pages/AdminUsers';
import AuditLog from './pages/AuditLog';

interface Session { me: Me | null; refresh: () => Promise<void>; signOut: () => Promise<void>; can: (perm: string) => boolean }
const SessionContext = createContext<Session>(null!);
export const useSession = () => useContext(SessionContext);

export default function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);
  const [needsSetup, setNeedsSetup] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();

  const refresh = useCallback(async () => {
    try { setMe(await api<Me>('/api/me')); } catch { setMe(null); }
  }, []);

  useEffect(() => {
    (async () => {
      try { setNeedsSetup((await api<{ firstAdminNeeded: boolean }>('/api/setup/status')).firstAdminNeeded); } catch { /* ignore */ }
      await refresh();
      setLoading(false);
    })();
  }, [refresh]);

  useEffect(() => {
    const onSignedOut = () => { setMe(null); navigate('/signin?expired=1', { replace: true }); };
    window.addEventListener('pas:signed-out', onSignedOut);
    return () => window.removeEventListener('pas:signed-out', onSignedOut);
  }, [navigate]);

  const signOut = useCallback(async () => {
    try { await api('/api/auth/logout', { body: {} }); } finally { setMe(null); navigate('/signin', { replace: true }); }
  }, [navigate]);

  const can = useCallback((perm: string) => !!me?.permissions.includes(perm), [me]);

  if (loading) return <div className="splash"><div className="brand-mark">Galenica</div><p>Loading…</p></div>;

  const session: Session = { me, refresh, signOut, can };
  const publicPaths = ['/signin', '/setup'];
  if (needsSetup && location.pathname !== '/setup') return <Navigate to="/setup" replace />;
  if (!me && !publicPaths.includes(location.pathname)) return <Navigate to="/signin" replace state={{ from: location.pathname }} />;

  return (
    <SessionContext.Provider value={session}>
      <Routes>
        <Route path="/setup" element={<FirstAdmin onDone={() => { setNeedsSetup(false); navigate('/signin', { replace: true }); }} />} />
        <Route path="/signin" element={me ? <Navigate to="/" replace /> : <SignIn />} />
        <Route element={<Shell />}>
          <Route path="/" element={<Home />} />
          <Route path="/patients" element={<PatientSearch />} />
          <Route path="/patients/new" element={<PatientRegister />} />
          <Route path="/patients/:id" element={<PatientView />} />
          <Route path="/admin/users" element={<AdminUsers />} />
          <Route path="/audit" element={<AuditLog />} />
          <Route path="*" element={<div className="card"><h1>Page not found</h1><p>Use the menu to find what you need.</p></div>} />
        </Route>
      </Routes>
    </SessionContext.Provider>
  );
}
