import { inject, Injectable, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AppNotification } from './models';

/**
 * Notifications are produced asynchronously from claim events, so the UI polls for them.
 * Polling keeps this simple; server-sent events would be the next step.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private api = inject(ApiService);
  private timer: ReturnType<typeof setInterval> | null = null;

  readonly items = signal<AppNotification[]>([]);
  readonly unread = signal(0);
  /** Bumped whenever new notifications arrive, so open pages can refresh themselves. */
  readonly changed = signal(0);

  start(intervalMs = 10_000): void {
    this.stop();
    this.refresh();
    this.timer = setInterval(() => this.refresh(), intervalMs);
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.items.set([]);
    this.unread.set(0);
  }

  async refresh(): Promise<void> {
    try {
      const list = await this.api.notifications();
      const newest = list.items[0]?.id;
      if (newest && newest !== this.items()[0]?.id && this.items().length > 0) this.changed.update(n => n + 1);
      this.items.set(list.items);
      this.unread.set(list.unreadCount);
    } catch {
      /* transient; next poll will retry */
    }
  }

  async markRead(id: string): Promise<void> {
    await this.api.markRead(id);
    await this.refresh();
  }

  async markAllRead(): Promise<void> {
    await this.api.markAllRead();
    await this.refresh();
  }
}
