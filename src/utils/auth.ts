export async function checkAuth() {
  try {
    const response = await fetch('/api/auth/login');

    if (response.ok) {
      const data = await response.json();
      return { isAuthenticated: true, user: data };
    }

    return { isAuthenticated: false, user: null };
  } catch {
    return { isAuthenticated: false, user: null };
  }
}

export async function logout() {
  try {
    await fetch('/api/auth/login', {
      method: 'DELETE'
    });
    return true;
  } catch {
    return false;
  }
}
