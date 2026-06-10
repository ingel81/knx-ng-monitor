import { KnxTelegram } from '../services/signalr.service';

/** History rows have the same shape as live telegrams. */
export type TelegramHistoryRow = KnxTelegram;

export interface TelegramQueryParams {
  from?: string;       // ISO-8601
  to?: string;         // ISO-8601
  address?: string;    // destination group address
  source?: string;     // source individual address
  type?: string;       // MessageType name (Write/Read/Response)
  types?: string;      // CSV of MessageType names (multi-select)
  q?: string;          // free-text search across fields
  order?: 'asc' | 'desc'; // timestamp sort direction (default desc)
  cursor?: string;     // keyset cursor; omit for first page
  pageSize: number;
}

export interface TelegramPage {
  items: KnxTelegram[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface ArchiveDay {
  date: string;        // YYYY-MM-DD
  fileName: string;
  sizeBytes: number;
  compressed: boolean;
}
