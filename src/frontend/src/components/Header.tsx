'use client';

import { useEffect } from 'react';
import { useAuth, useClerk, useUser } from '@clerk/nextjs';

export default function Header() {
  const { openSignIn, signOut } = useClerk();
  const { isLoaded, isSignedIn, user } = useUser();
  const { getToken } = useAuth();

  useEffect(() => {
    if (!isSignedIn) return;
    (async () => {
      try {
        const token = await getToken();
        await fetch('/api/auth/sync', {
          method: 'POST',
          headers: { Authorization: `Bearer ${token}` },
        });
      } catch (err) {
        console.error('[auth/sync] failed:', err);
      }
    })();
  }, [isSignedIn, getToken]);

  if (!isLoaded) return <header className="flex items-center justify-end gap-4 p-4" />;

  return (
    <header className="flex items-center justify-end gap-4 p-4">
      {isSignedIn ? (
        <>
          <span className="text-sm">{user.primaryEmailAddress?.emailAddress}</span>
          <button onClick={() => signOut()} className="text-sm font-medium">
            Log out
          </button>
        </>
      ) : (
        <button onClick={() => openSignIn()} className="text-sm font-medium">
          Sign in
        </button>
      )}
    </header>
  );
}
