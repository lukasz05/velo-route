'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth, useClerk, useUser } from '@clerk/nextjs';
import ConfirmModal from '@/components/ConfirmModal';

export default function AccountPage() {
  const { isLoaded, isSignedIn, user } = useUser();
  const { getToken } = useAuth();
  const { openSignIn, signOut } = useClerk();
  const router = useRouter();

  const [showConfirm, setShowConfirm] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  useEffect(() => {
    if (!isLoaded) return;
    if (!isSignedIn) {
      router.replace('/');
      openSignIn();
    }
  }, [isLoaded, isSignedIn, openSignIn, router]);

  async function handleConfirmDelete() {
    setIsDeleting(true);
    setDeleteError(null);
    try {
      const token = await getToken();
      const res = await fetch('/api/account', {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.status !== 204) throw new Error(`Delete failed: ${res.status}`);
      await signOut();
      router.push('/?accountDeleted=1');
    } catch {
      setDeleteError('Could not delete your account. Please try again.');
      setShowConfirm(false);
    } finally {
      setIsDeleting(false);
    }
  }

  if (!isLoaded || !isSignedIn) return null;

  return (
    <div className="max-w-3xl p-6">
      <h1 className="text-xl font-bold text-zinc-900">Account</h1>

      <div className="mt-6">
        <span className="text-sm font-medium text-zinc-500">Email</span>
        <p className="mt-1 text-sm text-zinc-900">{user.primaryEmailAddress?.emailAddress}</p>
      </div>

      {deleteError && <p className="mt-4 text-sm text-red-600">{deleteError}</p>}

      <div className="mt-10 rounded-lg border border-red-200 p-5">
        <h2 className="text-sm font-semibold text-zinc-900">Delete account</h2>
        <p className="mt-1 text-sm text-zinc-600">
          Permanently delete your account and all saved routes. This cannot be undone.
        </p>
        <button
          onClick={() => setShowConfirm(true)}
          className="mt-4 rounded-md border border-red-300 bg-white px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50"
        >
          Delete Account
        </button>
      </div>

      {showConfirm && (
        <ConfirmModal
          title="Delete account?"
          message='This permanently deletes your account and all saved routes. Type "DELETE" to confirm.'
          confirmLabel="Delete"
          confirmationPhrase="DELETE"
          isConfirming={isDeleting}
          onConfirm={handleConfirmDelete}
          onCancel={() => setShowConfirm(false)}
        />
      )}
    </div>
  );
}
