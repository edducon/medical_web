import { KeyRound, ShieldCheck } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { setup, type Session } from '../api'

type SetupPageProps = { onConfigured: (session: Session) => void }

export function SetupPage({ onConfigured }: SetupPageProps) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [setupToken, setSetupToken] = useState('')
  const [error, setError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true); setError(undefined)
    try { onConfigured(await setup(email, password, setupToken)) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Не удалось создать администратора.') }
    finally { setSubmitting(false) }
  }

  return <main className="login-page"><section className="login-brand"><ShieldCheck size={36} /><h1>Первый запуск</h1><p>Создайте учётную запись администратора для защищённой работы с системой.</p></section><section className="login-card"><div><h2>Настройка доступа</h2><p>Одноразовый токен задан в переменной <code>SETUP_TOKEN</code> Docker.</p></div><form onSubmit={submit}><label>Email администратора<input type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} required /></label><label>Пароль<input type="password" autoComplete="new-password" minLength={12} value={password} onChange={(event) => setPassword(event.target.value)} required /></label><label>Одноразовый токен<input type="password" autoComplete="off" value={setupToken} onChange={(event) => setSetupToken(event.target.value)} required /></label>{error && <p className="login-error" role="alert">{error}</p>}<button className="button" type="submit" disabled={submitting}>{submitting ? 'Создание…' : 'Создать администратора'}</button></form><div className="login-protection"><KeyRound size={16} /> Токен действует только до первого запуска</div></section></main>
}
