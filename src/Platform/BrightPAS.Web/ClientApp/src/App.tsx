import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { Navigate, Route, Routes, useLocation, useNavigate } from 'react-router';
import { api, Me, Perm } from './api';
import Cssd from './pages/Cssd';
import Assets from './pages/Assets';
import Emr from './pages/Emr';
import EmrEpisode from './pages/EmrEpisode';
import Endoscopy from './pages/Endoscopy';
import EndoscopyProcedure from './pages/EndoscopyProcedure';
import Scribe from './pages/Scribe';
import ScribeSession from './pages/ScribeSession';
import Devices from './pages/Devices';
import { Quality, QualityComplaint, QualityIncident, QualityMine, ReportIncident } from './pages/Quality';
import Stock from './pages/Stock';
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
import Coding, { CodingWorkbench } from './pages/Coding';
import DymoLabels from './pages/DymoLabels';
import Estimates, { EstimateEdit, EstimatesOutbox } from './pages/Estimates';
import EstimatesAdmin from './pages/EstimatesAdmin';
import DayBoard from './pages/DayBoard';
import Outbox from './pages/Outbox';
import FormDesigner, { FormEntryPage, FormVersionEditor } from './pages/FormDesigner';
import Research, { ResearchExtract } from './pages/Research';
import FollowUp, { FollowUpContact, FollowUpPlans, FollowUpReport, PatientCheckIn, SsiCase, SsiList } from './pages/FollowUp';
import Triage, { TriageAssessment, TriageReport, TriageRules } from './pages/Triage';
import { MessagingDashboard, MessagingInterfaces, MessagingMessage, MessagingMessages, MessagingSettings, MessagingTables } from './pages/Messaging';
import { FlowEditor, MessagingFlows } from './pages/FlowDesigner';
import { MappingEditor, MessagingMappings } from './pages/MappingDesigner';

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
  if (location.pathname.startsWith('/c/')) return <Routes><Route path="/c/:token" element={<PatientCheckIn />} /></Routes>;
  if (location.pathname.startsWith('/my/')) return <Routes><Route path="/my/:token" element={<MyPortal />} /></Routes>;
  if (loading) return <div className="splash"><div className="brand-mark">BrightPAS</div><p>Loading…</p></div>;

  const session: Session = { me, refresh, signOut, can, mod, writable };
  // 0.12.4: a page typed into the address bar that this person may not use says so, instead of failing on refused server calls.
  const allowed = (ok: boolean, page: React.ReactElement) => ok ? page
    : <div className="card"><h1>Not available to you</h1><p>Your account does not include this page. If you need it for your work, ask your BrightPAS administrator.</p></div>;
  const triageAny = ['triage.view', 'triage.assess', 'triage.review', 'triage.rules', 'triage.approve'].some(can);
  const followAny = ['followup.view', 'followup.record', 'followup.plans', 'followup.ssi', 'followup.report'].some(can);
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
          <Route path="/patients" element={allowed(can(Perm.PatientView), <PatientSearch />)} />
          <Route path="/patients/new" element={allowed(can(Perm.PatientRegister), <PatientRegister />)} />
          <Route path="/patients/:id" element={allowed(can(Perm.PatientView), <PatientView />)} />
          <Route path="/admin/users" element={allowed(can(Perm.UsersManage), <AdminUsers />)} />
          <Route path="/audit" element={allowed(can(Perm.AuditView), <AuditLog />)} />
          <Route path="/admin/licence" element={allowed(can(Perm.ModulesManage), <Licence />)} />
          <Route path="/admin/modules/:id" element={allowed(can(Perm.ModulesManage), <ModuleSetup />)} />
          <Route path="/admin/referral-targets" element={<ReferralTargets />} />
          <Route path="/referrals" element={allowed(mod('referrals') && can(Perm.ReferralsView), <Referrals />)} />
          <Route path="/admin/organisation" element={allowed(can(Perm.OrganisationManage), <Organisation />)} />
          <Route path="/admin/roles" element={allowed(can(Perm.UsersManage), <Roles />)} />
          <Route path="/followup" element={allowed(mod('followup') && followAny, <FollowUp />)} />
          <Route path="/followup/ssi" element={allowed(mod('followup') && followAny, <SsiList />)} />
          <Route path="/followup/ssi/:id" element={allowed(mod('followup') && followAny, <SsiCase />)} />
          <Route path="/followup/plans" element={allowed(mod('followup') && followAny, <FollowUpPlans />)} />
          <Route path="/followup/report" element={allowed(mod('followup') && can(Perm.FollowUpReport), <FollowUpReport />)} />
          <Route path="/followup/:id" element={allowed(mod('followup') && followAny, <FollowUpContact />)} />
          <Route path="/research" element={allowed(mod('reporting') && (can(Perm.ResearchBuild) || can(Perm.ResearchApprove)), <Research />)} />
          <Route path="/research/:id" element={allowed(mod('reporting') && (can(Perm.ResearchBuild) || can(Perm.ResearchApprove)), <ResearchExtract />)} />
          <Route path="/forms/designer" element={allowed(mod('forms') && (can(Perm.FormsDesign) || can(Perm.FormsApprove) || can(Perm.FormsFill)), <FormDesigner />)} />
          <Route path="/forms/designer/:id" element={allowed(mod('forms') && (can(Perm.FormsDesign) || can(Perm.FormsApprove) || can(Perm.FormsFill)), <FormVersionEditor />)} />
          <Route path="/forms/entry/:id" element={allowed(mod('forms') && (can(Perm.FormsDesign) || can(Perm.FormsApprove) || can(Perm.FormsFill)), <FormEntryPage />)} />
          <Route path="/outbox" element={allowed((mod('reminders') && can(Perm.RemindersView)) || (mod('letters') && can(Perm.LettersView)), <Outbox />)} />
          <Route path="/day" element={allowed(mod('theatre') && can(Perm.TheatreView), <DayBoard />)} />
          <Route path="/triage" element={allowed(mod('triage') && triageAny, <Triage />)} />
          <Route path="/triage/rules" element={allowed(mod('triage') && triageAny, <TriageRules />)} />
          <Route path="/triage/report" element={allowed(mod('triage') && triageAny, <TriageReport />)} />
          <Route path="/triage/:id" element={allowed(mod('triage') && triageAny, <TriageAssessment />)} />
          <Route path="/estimates" element={allowed(mod('estimates') && (can(Perm.EstimatesBuild) || can(Perm.EstimatesManage)), <Estimates />)} />
          <Route path="/estimates/new" element={allowed(mod('estimates') && can(Perm.EstimatesBuild), <EstimateEdit />)} />
          <Route path="/estimates/outbox" element={allowed(mod('estimates') && can(Perm.EstimatesBuild), <EstimatesOutbox />)} />
          <Route path="/estimates/admin" element={allowed(mod('estimates') && can(Perm.EstimatesManage), <EstimatesAdmin />)} />
          <Route path="/estimates/:id" element={allowed(mod('estimates') && (can(Perm.EstimatesBuild) || can(Perm.EstimatesManage)), <EstimateEdit />)} />
          <Route path="/theatre/templates" element={allowed(mod('theatre') && can(Perm.TheatreManage), <ListTemplates />)} />
          <Route path="/calendar" element={allowed(mod('theatre') && can(Perm.TheatreView), <Calendar />)} />
          <Route path="/roster" element={allowed(mod('rostering') && can(Perm.RosterView), <Roster />)} />
          <Route path="/letters" element={allowed(mod('letters') && can(Perm.LettersView), <Letters />)} />
          <Route path="/letters/new" element={allowed(mod('letters') && can(Perm.LettersWrite), <LetterNew />)} />
          <Route path="/letters/:id" element={allowed(mod('letters') && can(Perm.LettersView), <LetterView />)} />
          <Route path="/admin/letters" element={allowed(mod('letters') && can(Perm.LettersTemplates), <LetterAdmin />)} />
          <Route path="/waitlist" element={allowed(mod('referrals') && can(Perm.ReferralsView), <Waitlist />)} />
          <Route path="/admin/interfaces" element={allowed(can(Perm.Integration), <Interfaces />)} />
          <Route path="/messaging" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingDashboard />)} />
          <Route path="/messaging/interfaces" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingInterfaces />)} />
          <Route path="/messaging/messages" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingMessages />)} />
          <Route path="/messaging/messages/:id" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingMessage />)} />
          <Route path="/messaging/flows" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingFlows />)} />
          <Route path="/messaging/flows/:id" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <FlowEditor />)} />
          <Route path="/messaging/mappings" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingMappings />)} />
          <Route path="/messaging/mappings/:id" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MappingEditor />)} />
          <Route path="/messaging/tables" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingTables />)} />
          <Route path="/messaging/settings" element={allowed(mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)), <MessagingSettings />)} />
          <Route path="/labs" element={allowed(mod('labs') && can(Perm.LabsView), <LabsInbox />)} />
          <Route path="/labs/:id" element={allowed(mod('labs') && can(Perm.LabsView), <LabReport />)} />
          <Route path="/theatre" element={allowed(mod('theatre') && can(Perm.TheatreView), <TheatreLists />)} />
          <Route path="/forms" element={allowed(mod('forms') && (can(Perm.FormsTriage) || can(Perm.FormsSend)), <FormsList />)} />
          <Route path="/forms/:id" element={allowed(mod('forms') && (can(Perm.FormsTriage) || can(Perm.FormsSend)), <FormTriage />)} />
          <Route path="/billing" element={allowed(mod('billing') && can(Perm.BillingView), <Billing />)} />
          <Route path="/billing/new" element={allowed(mod('billing') && can(Perm.BillingView), <BillingNew />)} />
          <Route path="/billing/:id" element={allowed(mod('billing') && can(Perm.BillingView), <BillingDocument />)} />
          <Route path="/reminders" element={allowed(mod('reminders') && can(Perm.RemindersView), <Reminders />)} />
          <Route path="/patients/:id/transfer" element={allowed(can(Perm.PatientView), <TransferPack />)} />
          <Route path="/admin/identity" element={allowed(can(Perm.PatientMerge), <IdentityQueue />)} />
          <Route path="/credentialing" element={allowed(mod('credentialing') && can(Perm.CredentialingView), <Credentialing />)} />
          <Route path="/portal" element={allowed(mod('portal') && can(Perm.PortalSpecialist), <SpecialistPortal />)} />
          <Route path="/portal/requests" element={allowed(mod('portal') && can(Perm.PortalManage), <PortalRequests />)} />
          <Route path="/admin/security" element={allowed((can(Perm.UsersManage) || can(Perm.OrganisationManage)), <SecuritySettings />)} />
          <Route path="/emr" element={allowed(mod('emr') && can(Perm.EmrView), <Emr />)} />
          <Route path="/emr/:bookingId" element={allowed(mod('emr') && can(Perm.EmrView), <EmrEpisode />)} />
          <Route path="/scribe" element={allowed(mod('scribe') && (can(Perm.ScribeUse) || can(Perm.ScribeManage) || can(Perm.ScribeApprove)), <Scribe />)} />
          <Route path="/devices" element={allowed(mod('devices') && (can(Perm.DevicesUse) || can(Perm.DevicesManage)), <Devices />)} />
          <Route path="/quality" element={allowed(mod('quality') && (can(Perm.QualityView) || can(Perm.QualityManage)), <Quality />)} />
          <Route path="/quality/incidents/:id" element={allowed(mod('quality') && (can(Perm.QualityView) || can(Perm.QualityManage)), <QualityIncident />)} />
          <Route path="/quality/complaints/:id" element={allowed(mod('quality') && (can(Perm.QualityView) || can(Perm.QualityManage)), <QualityComplaint />)} />
          <Route path="/quality/report" element={allowed(mod('quality') && can(Perm.QualityReport), <ReportIncident />)} />
          <Route path="/coding" element={allowed(mod('reporting') && (can(Perm.CodingCode) || can(Perm.CodingSubmit) || can(Perm.CodingManage)), <Coding />)} />
          <Route path="/coding/events/:id" element={allowed(mod('reporting') && (can(Perm.CodingCode) || can(Perm.CodingSubmit) || can(Perm.CodingManage)), <CodingWorkbench />)} />
          <Route path="/quality/mine" element={allowed(mod('quality') && can(Perm.QualityReport), <QualityMine />)} />
          <Route path="/scribe/:id" element={allowed(mod('scribe') && can(Perm.ScribeUse), <ScribeSession />)} />
          <Route path="/endoscopy" element={allowed(mod('endoscopy') && can(Perm.EndoView), <Endoscopy />)} />
          <Route path="/endoscopy/:bookingId" element={allowed(mod('endoscopy') && can(Perm.EndoView), <EndoscopyProcedure />)} />
          <Route path="/cssd" element={allowed(mod('assets') && can(Perm.AssetsView), <Cssd />)} />
          <Route path="/assets" element={allowed(mod('assets') && can(Perm.AssetsView), <Assets />)} />
          <Route path="/stock" element={allowed(mod('assets') && can(Perm.AssetsView), <Stock />)} />
          <Route path="/waitlist/nbrs" element={allowed(mod('referrals') && can(Perm.ReferralsView), <Nbrs />)} />
          <Route path="*" element={<div className="card"><h1>Page not found</h1><p>Use the menu to find what you need.</p></div>} />
        </Route>
      </Routes>
    </SessionContext.Provider>
  );
}
