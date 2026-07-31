import { Info, TriangleAlert } from 'lucide-react'
import type { Isotope, PatientPlan } from '../types'
import { isotopeConfig } from '../data'

type ActivityPanelProps = { isotope: Isotope; sourceActivity: number; plans: PatientPlan[]; onIsotopeChange: (value: Isotope) => void; onSourceActivityChange: (value: number) => void; onSaveSourceActivity: () => void; savingActivity: boolean; isSaved: boolean }
const startOfDay = 8 * 60

function remainingAt(activity: number, isotope: Isotope, plans: PatientPlan[], moment: number) {
  const halfLife = isotopeConfig[isotope].halfLife
  const ordered = plans.filter((plan) => plan.startMinutes <= moment).slice().sort((a, b) => a.startMinutes - b.startMinutes)
  let result = activity
  let previous = startOfDay
  for (const plan of ordered) { result *= 2 ** (-(plan.startMinutes - previous) / halfLife); result -= plan.weight * isotopeConfig[isotope].coefficient; previous = plan.startMinutes }
  return Math.max(0, result * 2 ** (-(moment - previous) / halfLife))
}
export function doseFor(plan: PatientPlan) { return Math.round(plan.weight * isotopeConfig[plan.isotope].coefficient) }

export function ActivityPanel({ isotope, sourceActivity, plans, onIsotopeChange, onSourceActivityChange, onSaveSourceActivity, savingActivity, isSaved }: ActivityPanelProps) {
  const points = Array.from({ length: 9 }, (_, index) => ({ x: index, activity: remainingAt(sourceActivity, isotope, plans, startOfDay + index * 60) }))
  const max = Math.max(sourceActivity, 1)
  const path = points.map(({ x, activity }, index) => `${index === 0 ? 'M' : 'L'} ${x * 12.5} ${100 - (activity / max) * 88}`).join(' ')
  const current = Math.round(remainingAt(sourceActivity, isotope, plans, 12 * 60 + 30))
  const warning = current < sourceActivity * 0.25
  return <section className="panel activity-panel" aria-labelledby="activity-title"><div className="activity-title-row"><h2 id="activity-title">Активность РФП</h2><select aria-label="Изотоп для графика" value={isotope} onChange={(event) => onIsotopeChange(event.target.value as Isotope)}><option>F-18</option><option>Ga-68</option></select></div><div className="activity-stats"><div><span>Расчётная активность в 12:30</span><strong>{current.toLocaleString('ru-RU')} <small>МБк</small></strong><em>{isSaved ? 'Расчёт с учётом плана смены' : 'Введите активность и добавьте пациента'}</em></div><div><span>Активность при поставке</span><strong>{sourceActivity.toLocaleString('ru-RU')} <small>МБк</small></strong><input aria-label="Фактическая активность при поставке" type="number" min="0" value={sourceActivity} onChange={(event) => onSourceActivityChange(Number(event.target.value))} />{isSaved && <button className="text-button" type="button" onClick={onSaveSourceActivity} disabled={savingActivity}>{savingActivity ? 'Сохранение…' : 'Сохранить факт'}</button>}</div></div><div className="chart" aria-label="График остаточной активности"><svg viewBox="0 0 100 112" preserveAspectRatio="none" role="img">{[16, 40, 64, 88].map((y) => <line key={y} x1="0" x2="100" y1={y} y2={y} />)}<path d={path} />{points.map(({ x, activity }) => <circle key={x} cx={x * 12.5} cy={100 - (activity / max) * 88} r="1.8" />)}</svg><div className="chart-axis"><span>08:00</span><span>10:00</span><span>12:00</span><span>14:00</span><span>16:00</span></div></div>{warning && <div className="alert"><TriangleAlert size={18} /><span><strong>Ожидается низкий остаток активности.</strong> Проверьте план до подтверждения.</span></div>}<div className="activity-foot"><span><Info size={16} /> Период полураспада: <strong>{isotopeConfig[isotope].halfLife} мин</strong></span><span>Источник: поставка РФП</span></div></section>
}
