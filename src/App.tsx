import { useState, useEffect } from 'react';
import Login from './components/Login';
import FileUpload from './components/FileUpload';
import { checkAuth, logout } from './utils/auth';
import './App.css';

export default function App() {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isChecking, setIsChecking] = useState(true);
  const [username, setUsername] = useState('');

  useEffect(() => {
    checkAuthStatus();
  }, []);

  const checkAuthStatus = async () => {
    setIsChecking(true);
    const result = await checkAuth();
    setIsAuthenticated(result.isAuthenticated);
    if (result.user) {
      setUsername(result.user.username);
    }
    setIsChecking(false);
  };

  const handleLoginSuccess = async () => {
    await checkAuthStatus();
  };

  const handleLogout = async () => {
    await logout();
    setIsAuthenticated(false);
    setUsername('');
  };

  if (isChecking) {
    return (
      <div className="loading">
        <p>Loading...</p>
      </div>
    );
  }

  return (
    <>
      {isAuthenticated && (
        <div className="logout-bar">
          Logged in as: <strong>{username}</strong> |{' '}
          <button onClick={handleLogout} className="logout-button">
            Logout
          </button>
        </div>
      )}

      {isAuthenticated ? <FileUpload /> : <Login onLoginSuccess={handleLoginSuccess} />}
    </>
  );
}
