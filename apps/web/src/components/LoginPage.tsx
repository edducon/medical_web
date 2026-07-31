import { Activity, LockKeyhole } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { login, type Session } from '../api'

type LoginPageProps = { onLoggedIn: (session: Session) => void; initialError?: string }

export function LoginPage({ onLoggedIn, initialError }: LoginPageProps) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(initialError)
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    setError(undefined)
    try { onLoggedIn(await login(email, password)) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Не удалось войти.') }
    finally { setSubmitting(false) }
  }

  return <main className="login-page"><section className="login-brand"><Activity size={36} /><h1>Радиоплан</h1><p>Планирование исследований и контроль активности РФП.</p></section><section className="login-card"><div><h2>Вход в систему</h2><p>Используйте учётную запись оператора или администратора.</p></div><form onSubmit={submit}><label>Email<input type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} required /></label><label>Пароль<input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required /></label>{error && <p className="login-error" role="alert">{error}</p>}<button className="button" type="submit" disabled={submitting}>{submitting ? 'Выполняется вход…' : 'Войти'}</button></form><div className="login-protection"><LockKeyhole size={16} /> Сеанс защищён шифрованием</div></section></main>
}
