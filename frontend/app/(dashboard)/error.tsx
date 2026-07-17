'use client';

import { useEffect } from 'react';

const CHUNK_RELOAD_KEY = 'teas-chunk-reload-attempted';

export default function DashboardError({ error }: { error: Error & { digest?: string } }) {
  useEffect(() => {
    const isChunkError = error.name === 'ChunkLoadError' || /Loading chunk .* failed/i.test(error.message);
    if (isChunkError && !sessionStorage.getItem(CHUNK_RELOAD_KEY)) {
      sessionStorage.setItem(CHUNK_RELOAD_KEY, '1');
      window.location.reload();
    }
  }, [error]);

  return (
    <div className="flex min-h-[50vh] items-center justify-center p-6 text-center">
      <div>
        <h2 className="text-xl font-bold">เกิดข้อผิดพลาด</h2>
        <p className="mt-2 text-base-content/70">ไม่สามารถแสดงหน้านี้ได้ กรุณาลองโหลดใหม่อีกครั้ง</p>
        <button className="btn btn-primary mt-4" onClick={() => window.location.reload()}>
          โหลดใหม่
        </button>
      </div>
    </div>
  );
}
