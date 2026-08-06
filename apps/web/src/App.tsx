import { FileDown, FlaskConical, LockKeyhole } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { addAppointment, closeShift, confirmAppointment, createShift, deleteAppointment, getIsotopeSettings, getScanners, getSession, getSetupStatus, getShift, logout, reportUrl, updateAppointment, updateSourceActivity, type ApiAppointment, type ApiShift, type Session } from './api'
import { isotopeConfig } from './data'
import { ActivityPanel } from './components/ActivityPanel'
import { AdminPage } from './components/AdminPage'
import { DatePicker } from './components/DatePicker'
import { HistoryPage } from './components/HistoryPage'
import { LoginPage } from './components/LoginPage'
import { NewPatientDialog } from './components/NewPatientDialog'
import { OpenShiftDialog } from './components/OpenShiftDialog'
import { PatientTable } from './components/PatientTable'
import { Planner } from './components/Planner'
import { Sidebar, type AppView } from './components/Sidebar'
import { SetupPage } from './components/SetupPage'
import type { Isotope, PatientPlan, Scanner } from './types'

const isotopes: Isotope[] = ['F-18', 'Ga-68']
const today = () => new Intl.DateTimeFormat('en-CA').format(new Date())
const toIso = (date: string, minutes: number) => new Date(`${date}T${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}:00`).toISOString()
const minutesAt = (value: string) => { const date = new Date(value); return date.getHours() * 60 + date.getMinutes() }
const localDateTime = (value: string) => { const date = new Date(value); date.setMinutes(date.getMinutes() - date.getTimezoneOffset()); return date.toISOString().slice(0, 16) }
function appointmentToPlan(item: ApiAppointment, coefficient: number): PatientPlan { return { id: item.id, number: item.patientNumber, weight: item.weightKg, isotope: item.isotopeCode, protocol: item.protocolName, scannerId: item.scannerId, injectionMinutes: minutesAt(item.injectionAt), scanStartMinutes: minutesAt(item.scanStartAt), duration: item.durationMinutes, uptakeMinutes: item.uptakeMinutes, patientCategory: item.patientCategory, coefficient, confirmed: item.confirmed } }

function Dashboard({ session, onLogout }: { session: Session; onLogout: () => void }) {
  const [scanners, setScanners] = useState<Scanner[]>([])
  const [plans, setPlans] = useState<PatientPlan[]>([])
  const [shifts, setShifts] = useState<Partial<Record<Isotope, ApiShift | null>>>({})
  const [date, setDate] = useState(today)
  const [isotope, setIsotope] = useState<Isotope>('F-18')
  const [activity, setActivity] = useState(37000)
  const [measuredAt, setMeasuredAt] = useState(`${today()}T08:00`)
  const [dialog, setDialog] = useState(false)
  const [editing, setEditing] = useState<PatientPlan>()
  const [notice, setNotice] = useState<string>()
  const [view, setView] = useState<AppView>('plan')
  const [setupOpen, setSetupOpen] = useState(false)
  const [shiftsLoading, setShiftsLoading] = useState(true)
  const activeShift = shifts[isotope]
  const openIsotopes = isotopes.filter((item) => Boolean(shifts[item] && !shifts[item]?.isClosed))
  const missingIsotopes = isotopes.filter((item) => !shifts[item])
  const calculation = activeShift ?? { halfLifeMinutes: isotopeConfig[isotope].halfLife, doseCoefficientMbqPerKg: isotopeConfig[isotope].coefficient }

  useEffect(() => {
    setShiftsLoading(true)
    void Promise.all([getScanners(), getIsotopeSettings(), ...isotopes.map((item) => getShift(date, item))]).then(([scannerRows, settings, f18, ga68]) => {
      settings.forEach((item) => { isotopeConfig[item.isotopeCode].halfLife = item.halfLifeMinutes; isotopeConfig[item.isotopeCode].coefficient = item.doseCoefficientMbqPerKg; isotopeConfig[item.isotopeCode].defaultActivity = item.defaultSourceActivityMbq })
      setScanners(scannerRows); setShifts({ 'F-18': f18, 'Ga-68': ga68 })
      setPlans([...(f18?.appointments.map((item) => appointmentToPlan(item, f18.doseCoefficientMbqPerKg)) ?? []), ...(ga68?.appointments.map((item) => appointmentToPlan(item, ga68.doseCoefficientMbqPerKg)) ?? [])])
    }).catch((error: unknown) => setNotice(error instanceof Error ? error.message : 'Не удалось загрузить смену.')).finally(() => setShiftsLoading(false))
  }, [date])

  useEffect(() => { setActivity(activeShift?.sourceActivityMbq ?? isotopeConfig[isotope].defaultActivity); setMeasuredAt(activeShift ? localDateTime(activeShift.sourceMeasuredAt) : `${date}T08:00`) }, [activeShift, date, isotope])
  useEffect(() => { if (!shiftsLoading) setSetupOpen(date <= today() && !Boolean(shifts['F-18'] || shifts['Ga-68'])) }, [date, shifts, shiftsLoading])

  async function addPlan(plan: PatientPlan) {
    const draft = { scannerId: plan.scannerId, patientNumber: plan.number, weightKg: plan.weight, protocolName: plan.protocol, scanStartAt: toIso(date, plan.scanStartMinutes), patientCategory: plan.patientCategory }
    const targetShift = shifts[plan.isotope]
    if (targetShift && !targetShift.isClosed) {
      const created = await addAppointment(targetShift.id, draft); const mapped = appointmentToPlan(created, targetShift.doseCoefficientMbqPerKg)
      setPlans((items) => [...items, mapped]); setShifts((items) => ({ ...items, [plan.isotope]: { ...targetShift, appointments: [...targetShift.appointments, created] } })); setIsotope(plan.isotope); return
    }
    throw new Error('Сначала откройте смену и укажите активность РФП.')
  }
  async function openShift(nextIsotope: Isotope, nextActivity: number, nextMeasuredAt: string) {
    const shift = await createShift(date, nextIsotope, nextActivity, new Date(nextMeasuredAt).toISOString())
    setShifts((items) => ({ ...items, [nextIsotope]: shift })); setIsotope(nextIsotope); setSetupOpen(false)
  }
  async function savePlan(plan: PatientPlan) {
    if (!editing) return addPlan(plan)
    const saved = await updateAppointment(plan.id, { scannerId: plan.scannerId, patientNumber: plan.number, weightKg: plan.weight, protocolName: plan.protocol, scanStartAt: toIso(date, plan.scanStartMinutes), patientCategory: plan.patientCategory })
    const shift = shifts[saved.isotopeCode]; setPlans((items) => items.map((item) => item.id === plan.id ? appointmentToPlan(saved, shift?.doseCoefficientMbqPerKg ?? isotopeConfig[saved.isotopeCode].coefficient) : item)); setEditing(undefined)
  }
  async function removePlan(plan: PatientPlan) { await deleteAppointment(plan.id); setPlans((items) => items.filter((item) => item.id !== plan.id)); setShifts((items) => ({ ...items, [plan.isotope]: items[plan.isotope] ? { ...items[plan.isotope]!, appointments: items[plan.isotope]!.appointments.filter((item) => item.id !== plan.id) } : null })) }
  async function saveActivity() {
    if (!activeShift) return
    try { await updateSourceActivity(activeShift.id, activity, new Date(measuredAt).toISOString(), calculation.halfLifeMinutes, calculation.doseCoefficientMbqPerKg); setShifts((items) => ({ ...items, [isotope]: { ...activeShift, sourceActivityMbq: activity, sourceMeasuredAt: new Date(measuredAt).toISOString() } })) }
    catch (error) { setNotice(error instanceof Error ? error.message : 'Не удалось сохранить параметры расчёта.') }
  }
  async function confirm(id: string) { try { await confirmAppointment(id); setPlans((items) => items.map((item) => item.id === id ? { ...item, confirmed: true } : item)) } catch (error) { setNotice(error instanceof Error ? error.message : 'Не удалось подтвердить запись.') } }
  async function closeCurrentShift() { if (!activeShift || activeShift.isClosed || !window.confirm('Закрыть смену? После закрытия изменения будут недоступны.')) return; try { await closeShift(activeShift.id); setShifts((items) => ({ ...items, [isotope]: { ...activeShift, isClosed: true } })) } catch (error) { setNotice(error instanceof Error ? error.message : 'Не удалось закрыть смену.') } }
  const selectedPlans = useMemo(() => plans.filter((item) => item.isotope === isotope), [plans, isotope])
  const shiftPanel = <ActivityPanel isotope={isotope} sourceActivity={activity} measuredAt={measuredAt} halfLifeMinutes={calculation.halfLifeMinutes} coefficient={calculation.doseCoefficientMbqPerKg} plans={selectedPlans} onIsotopeChange={setIsotope} onSourceActivityChange={setActivity} onMeasuredAtChange={setMeasuredAt} onSaveSourceActivity={saveActivity} savingActivity={false} isSaved={Boolean(activeShift)} />
  const page = view === 'settings' ? <AdminPage /> : view === 'history' ? <HistoryPage onSelect={(nextDate) => { setDate(nextDate); setView('plan') }} /> : <>
    <header className="topbar"><div><h1>План смены</h1><DatePicker value={date} onChange={setDate} /></div><div className="topbar-actions">{missingIsotopes.length > 0 && <button className="icon-button" type="button" title="Открыть смену для РФП" aria-label="Открыть смену для РФП" onClick={() => setSetupOpen(true)}><FlaskConical size={19} /></button>}{activeShift && !activeShift.isClosed && <button className="icon-button" type="button" title="Закрыть смену" aria-label="Закрыть смену" onClick={() => void closeCurrentShift()}><LockKeyhole size={18} /></button>}<button className="icon-button" type="button" title="Скачать PDF-отчёт" aria-label="Скачать PDF-отчёт" disabled={!activeShift} onClick={() => activeShift && window.open(reportUrl(activeShift.id), '_blank', 'noopener')}><FileDown size={19} /></button></div></header>
    {notice && <p className="dashboard-notice">{notice}</p>}
    {shiftPanel}
    <Planner scanners={scanners} plans={plans} onAddPatient={() => { setEditing(undefined); setDialog(true) }} disabled={!scanners.length || !openIsotopes.length} />
    <PatientTable plans={plans} canConfirm={Boolean(activeShift && !activeShift.isClosed)} onConfirm={confirm} onEdit={activeShift?.isClosed ? undefined : (plan) => { setEditing(plan); setIsotope(plan.isotope); setDialog(true) }} onDelete={activeShift?.isClosed ? undefined : removePlan} />
  </>
  return <div className="app-shell"><Sidebar email={session.email} isAdministrator={session.role === 'administrator'} onLogout={onLogout} view={view} onNavigate={setView} /><main className="main-content">{page}</main>{dialog && <NewPatientDialog scanners={scanners} isotope={isotope} availableIsotopes={openIsotopes} initialPlan={editing} onClose={() => { setDialog(false); setEditing(undefined) }} onSave={savePlan} />}{setupOpen && missingIsotopes.length > 0 && <OpenShiftDialog date={date} onClose={() => setSetupOpen(false)} onDateChange={setDate} defaults={{ 'F-18': isotopeConfig['F-18'].defaultActivity, 'Ga-68': isotopeConfig['Ga-68'].defaultActivity }} availableIsotopes={missingIsotopes} onOpen={openShift} />}</div>
}

export default function App() {
  const [session, setSession] = useState<Session | null>(null); const [loading, setLoading] = useState(true); const [error, setError] = useState<string>(); const [needsSetup, setNeedsSetup] = useState(false)
  useEffect(() => { void getSession().then(async (current) => { setSession(current); if (!current) setNeedsSetup((await getSetupStatus()).needsSetup) }).catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Не удалось подключиться к серверу.')).finally(() => setLoading(false)) }, [])
  if (loading) return <main className="loading-screen">Проверка защищённого сеанса…</main>
  if (!session && needsSetup) return <SetupPage onConfigured={setSession} />
  if (!session) return <LoginPage initialError={error} onLoggedIn={setSession} />
  return <Dashboard session={session} onLogout={() => { void logout().finally(() => setSession(null)) }} />
}
