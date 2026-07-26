import { Suspense } from 'react';
import RouteApp from '@/components/RouteApp';
import AccountDeletedBanner from '@/components/AccountDeletedBanner';

export default function Page() {
  return (
    <>
      <Suspense fallback={null}>
        <AccountDeletedBanner />
      </Suspense>
      <RouteApp />
    </>
  );
}

