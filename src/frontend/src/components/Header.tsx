'use client';

import { useClerk, useUser } from '@clerk/nextjs';

export default function Header() {
  const { openSignIn, signOut } = useClerk();
  const { isLoaded, isSignedIn, user } = useUser();

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
