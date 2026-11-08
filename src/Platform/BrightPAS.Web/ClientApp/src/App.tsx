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
import Licence from './pages/Licence';
import ModuleSetup from './pages/ModuleSetup';
import Referrals from './pages/Referrals';
import Waitlist from './pages/Waitlist';
import ReferralTargets from './pages/ReferralTargets';
import Organisation from './pages/Organisation';
import Roles from './pages/Roles';
import ListTemplates from './pages/ListTemplates';
import Letters, { LetterNew, LetterView } from './pages/Letters';
import LetterAdmin from './pages/LetterAdmin';
import Calendar from './pages/Calendar';
import Roster from './pages/Roster';
import Interfaces from './pages/Interfaces';
import LabsInbox from './pages/LabsInbox';
import LabReport from './pages/LabReport';
import TheatreLists from './pages/TheatreLists';
import FormsList from './pages/FormsList';
import FormTriage from './pages/FormTriage';
import PatientQuestionnaire from './pages/PatientQuestionnaire';
import Billing from './pages/Billing';
import BillingDocument from './pages/BillingDocument';
import BillingNew from './pages/BillingNew';
import Reminders from './pages/Reminders';
import TransferPack from './pages/TransferPack';
import IdentityQueue from './pages/IdentityQueue';
import Credentialing from './pages/Credentialing';
import SpecialistPortal from './pages/SpecialistPortal';
import PortalRequests from './pages/PortalRequests';
import MyPortal from './pages/MyPortal';
import SecuritySettings from './pages/SecuritySettings';
import Nbrs from './pages/Nbrs';
import DymoLabels from './pages/DymoLabels';

interface Session { me: Me | null; refresh: () => Promise<void>; signOut: () => Promise<void>; can: (perm: string) => boolean; mod: (id: string) => boolean; writable: (id: string) => boolean }
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
    if (location.pathname.startsWith('/q/') || location.pathname.startsWith('/my/')) { setLoading(false); return; }
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
  // A module appears once it is licensed AND set up (ADR 0014). Administrators also see licensed modules that still need set-up.
  const mod = useCallback((id: string) => !!me && (me.modules[id] ?? 'None') !== 'None'
    && (!!me.moduleSetup?.[id] || me.permissions.includes('admin.modules.manage')), [me]);
  const writable = useCallback((id: string) => me?.modules[id] === 'Full', [me]);

  if (location.pathname.startsWith('/q/')) return <Routes><Route path="/q/:token" element={<PatientQuestionnaire />} /></Routes>;
  if (location.pathname.startsWith('/my/')) return <Routes><Route path="/my/:token" element={<MyPortal />} /></Routes>;
  if (loading) return <div className="splash"><div className="brand-mark">BrightPAS</div><p>Loading…</p></div>;

  const session: Session = { me, refresh, signOut, can, mod, writable };
  const publicPaths = ['/signin', '/setup'];
  if (needsSetup && location.pathname !== '/setup') return <Navigate to="/setup" replace />;
  if (!me && !publicPaths.includes(location.pathname)) return <Navigate to="/signin" replace state={{ from: location.pathname }} />;

  return (
    <SessionContext.Provider value={session}>
      <Routes>
        <Route path="/setup" element={<FirstAdmin onDone={() => { setNeedsSetup(false); navigate('/signin', { replace: true }); }} />} />
        <Route path="/signin" element={me ? <Navigate to="/" replace /> : <SignIn />} />
        <Route path="/patients/:id/labels" element={<DymoLabels />} />
        <Route element={<Shell />}>
          <Route path="/" element={<Home />} />
          <Route path="/patients" element={<PatientSearch />} />
          <Route path="/patients/new" element={<PatientRegister />} />
          <Route path="/patients/:id" element={<PatientView />} />
          <Route path="/admin/users" element={<AdminUsers />} />
          <Route path="/audit" element={<AuditLog />} />
          <Route path="/admin/licence" element={<Licence />} />
          <Route path="/admin/modules/:id" element={<ModuleSetup />} />
          <Route path="/admin/referral-targets" element={<ReferralTargets />} />
          <Route path="/referrals" element={<Referrals />} />
          <Route path="/admin/organisation" element={<Organisation />} />
          <Route path="/admin/roles" element={<Roles />} />
          <Route path="/theatre/templates" element={<ListTemplates />} />
          <Route path="/calendar" element={<Calendar />} />
          <Route path="/roster" element={<Roster />} />
          <Route path="/letters" element={<Letters />} />
          <Route path="/letters/new" element={<LetterNew />} />
          <Route path="/letters/:id" element={<LetterView />} />
          <Route path="/admin/letters" element={<LetterAdmin />} />
          <Route path="/waitlist" element={<Waitlist />} />
          <Route path="/admin/interfaces" element={<Interfaces />} />
          <Route path="/labs" element={<LabsInbox />} />
          <Route path="/labs/:id" element={<LabReport />} />
          <Route path="/theatre" element={<TheatreLists />} />
          <Route path="/forms" element={<FormsList />} />
          <Route path="/forms/:id" element={<FormTriage />} />
          <Route path="/billing" element={<Billing />} />
          <Route path="/billing/new" element={<BillingNew />} />
          <Route path="/billing/:id" element={<BillingDocument />} />
          <Route path="/reminders" element={<Reminders />} />
          <Route path="/patients/:id/transfer" element={<TransferPack />} />
          <Route path="/admin/identity" element={<IdentityQueue />} />
          <Route path="/credentialing" element={<Credentialing />} />
          <Route path="/portal" element={<SpecialistPortal />} />
          <Route path="/portal/requests" element={<PortalRequests />} />
          <Route path="/admin/security" element={<SecuritySettings />} />
          <Route path="/waitlist/nbrs" element={<Nbrs />} />
          <Route path="*" element={<div className="card"><h1>Page not found</h1><p>Use the menu to find what you need.</p></div>} />
        </Route>
      </Routes>
    </SessionContext.Provider>
  );
}
