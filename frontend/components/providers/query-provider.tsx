'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiError } from '@/lib/api';

export function QueryProvider({ children }: { children: React.ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            refetchOnWindowFocus: false,
            // 4xx (auth/permission/validation) are deterministic — never
            // retry (Sprint 13d P2: no 403 retry loop). 5xx/network → once.
            retry: (failureCount, error) => {
              if (error instanceof ApiError
                  && error.status >= 400 && error.status < 500) return false;
              return failureCount < 1;
            },
          },
        },
      }),
  );

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
