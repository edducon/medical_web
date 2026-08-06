import { X } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { getProtocols, getScannerProfiles, searchPatients, type PatientSearchResult, type Protocol, type ScannerProfile } from '../api'
import type { Isotope, PatientPlan, Scanner } from '../types'

type Props = { scanners: Scanner[]; isotope: Isotope; availableIsotopes: Isotope[]; initialPlan?: PatientPlan; onClose: () => void; onSave: (plan: PatientPlan) => Promise<void> }
const toTime = (minutes: number) => `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`

export function NewPatientDialog({ scanners, isotope, availableIsotopes, initialPlan, onClose, onSave }: Props) {
  const [number, setNumber] = useState(initialPlan?.number ?? '')
  const [weight, setWeight] = useState(initialPlan?.weight ?? 70)
  const [scannerId, setScannerId] = useState(initialPlan?.scannerId ?? scanners[0]?.id ?? '')
  const [time, setTime] = useState(initialPlan ? toTime(initialPlan.scanStartMinutes) : '15:30')
  const [category, setCategory] = useState<'S' | 'M' | 'F'>(initialPlan?.patientCategory ?? 'M')
  const [selectedIsotope, setSelectedIsotope] = useState<Isotope>(initialPlan?.isotope ?? isotope)
  const [protocols, setProtocols] = useState<Protocol[]>([])
  const [profiles, setProfiles] = useState<ScannerProfile[]>([])
  const [protocolName, setProtocolName] = useState(initialPlan?.protocol ?? '')
  const [matches, setMatches] = useState<PatientSearchResult[]>([])
  const [error, setError] = useState<string>(); const [saving, setSaving] = useState(false)
  const availableProtocols = useMemo(() => protocols.filter((protocol) => protocol.isActive && protocol.isotopeCode === selectedIsotope), [protocols, selectedIsotope])
  const selectedProtocol = availableProtocols.find((protocol) => protocol.name === protocolName)
  const profile = profiles.find((item) => item.scannerId === scannerId && item.patientCategory === category)
  useEffect(() => { void Promise.all([getProtocols(), getScannerProfiles()]).then(([protocolRows, profileRows]) => { setProtocols(protocolRows); setProfiles(profileRows) }).catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Не удалось загрузить параметры.')) }, [])
  useEffect(() => { if (!initialPlan && availableProtocols.length && !availableProtocols.some((protocol) => protocol.name === protocolName)) setProtocolName(availableProtocols[0].name) }, [availableProtocols, initialPlan, protocolName])
  useEffect(() => {
    const query = number.trim()
    if (initialPlan || query.length < 2) { setMatches([]); return }
    let active = true
    const timer = window.setTimeout(() => {
      void searchPatients(query).then((rows) => { if (active) setMatches(rows) }).catch((reason: unknown) => { if (active) setError(reason instanceof Error ? reason.message : 'Не удалось найти пациента.') })
    }, 250)
    return () => { active = false; window.clearTimeout(timer) }
  }, [initialPlan, number])
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const [hours, minutes] = time.split(':').map(Number)
    if (!selectedProtocol || !profile) return setError('Выберите протокол и аппарат с настроенным профилем.')
    setSaving(true)
    try { await onSave({ id: initialPlan?.id ?? crypto.randomUUID(), number, weight, isotope: selectedIsotope, scannerId, protocol: protocolName, injectionMinutes: hours * 60 + minutes - selectedProtocol.uptakeMinutes, scanStartMinutes: hours * 60 + minutes, duration: profile.preparationMinutes + profile.scanMinutes, uptakeMinutes: selectedProtocol.uptakeMinutes, patientCategory: category, coefficient: initialPlan?.coefficient ?? 0, confirmed: initialPlan?.confirmed ?? false }); onClose() }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Не удалось сохранить запись.') } finally { setSaving(false) }
  }
  const [hours, minutes] = time.split(':').map(Number); const injection = selectedProtocol ? toTime(hours * 60 + minutes - selectedProtocol.uptakeMinutes) : '—'
  return <div className="modal-backdrop"><dialog className="dialog" open><div className="dialog-title"><h2>{initialPlan ? 'Изменить запись' : 'Добавить пациента'}</h2><button className="icon-button" type="button" onClick={onClose} title="Закрыть" aria-label="Закрыть"><X size={20}/></button></div><form onSubmit={submit}><label>Номер пациента<span className="patient-number-field"><input value={number} onChange={(event) => setNumber(event.target.value)} required /></span></label>{matches.map((patient) => <button className="patient-match" type="button" key={patient.id} onClick={() => { setNumber(patient.patientNumber); setWeight(patient.lastWeightKg); setMatches([]) }}>{patient.patientNumber}<span>последний вес {patient.lastWeightKg} кг</span></button>)}<label>Вес, кг<input type="number" min="1" max="350" value={weight} onChange={(event) => setWeight(Number(event.target.value))} required /></label><label>Изотоп{initialPlan ? <input value={selectedIsotope} readOnly /> : <select value={selectedIsotope} onChange={(event) => setSelectedIsotope(event.target.value as Isotope)}>{availableIsotopes.map((item) => <option key={item}>{item}</option>)}</select>}</label><label>Протокол<select value={protocolName} onChange={(event) => setProtocolName(event.target.value)} required><option value="">Выберите протокол</option>{availableProtocols.map((protocol) => <option key={protocol.id} value={protocol.name}>{protocol.name} · накопление {protocol.uptakeMinutes} мин</option>)}</select></label><label>Аппарат<select value={scannerId} onChange={(event) => setScannerId(event.target.value)} required>{scanners.map((scanner) => <option key={scanner.id} value={scanner.id}>{scanner.name}</option>)}</select></label><label>Категория пациента<select value={category} onChange={(event) => setCategory(event.target.value as 'S' | 'M' | 'F')}><option value="S">S</option><option value="M">M</option><option value="F">F</option></select></label><label>Начало исследования<input type="time" value={time} onChange={(event) => setTime(event.target.value)} required /></label><p className="calculation-preview">Инъекция <strong>{injection}</strong><span>накопление {selectedProtocol?.uptakeMinutes ?? '—'} мин · аппарат {profile ? `${profile.preparationMinutes + profile.scanMinutes} мин` : 'не настроен'}</span></p>{error && <p className="login-error">{error}</p>}<div className="dialog-actions"><button className="button button--secondary" type="button" onClick={onClose}>Отмена</button><button className="button" disabled={saving || !selectedProtocol || !profile}>{saving ? 'Сохранение…' : 'Сохранить'}</button></div></form></dialog></div>
}
