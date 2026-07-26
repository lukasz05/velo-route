'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';

export default function AccountDeletedBanner() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const [show, setShow] = useState(false);

  useEffect(() => {
    if (searchParams.get('accountDeleted') === '1') {
      setShow(true);
      router.replace('/');
    }
  }, [searchParams, router]);

  if (!show) return null;

  return (
    <div className="bg-green-50 p-4 text-center text-sm text-green-800">
      Your account and all data have been deleted.
    </div>
  );
}
