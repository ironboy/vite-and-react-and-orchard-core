import { useState } from 'react';

interface LoginProps {
  onLoginSuccess: () => void;
}

export default function Login({ onLoginSuccess }: LoginProps) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);

    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ usernameOrEmail: username, password })
      });

      if (!response.ok) {
        throw new Error('Login failed');
      }

      onLoginSuccess();
    } catch (err) {
      setError('Invalid username or password');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="container-small">
      <h1>Login</h1>

      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label className="form-label">Username or Email:</label>
          <input
            type="text"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            disabled={isLoading}
            className="form-input"
            required
          />
        </div>

        <div className="form-group">
          <label className="form-label">Password:</label>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={isLoading}
            className="form-input"
            required
          />
        </div>

        {error && <div className="status-error">{error}</div>}

        <button type="submit" disabled={isLoading} className="form-button-full">
          {isLoading ? 'Logging in...' : 'Login'}
        </button>
      </form>

      <p className="hint-text">Default credentials: tom / Abcd1234!</p>
    </div>
  );
}
