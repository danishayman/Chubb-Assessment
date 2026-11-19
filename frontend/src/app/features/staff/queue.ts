import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, ClaimFilter, errorMessage } from '../../core/api.service';
import { ClaimStatus, ClaimSummary, Market } from '../../core/models';
import { NotificationsService } from '../../core/notifications.service';
import { ALL_STATUSES, MARKETS, money, STATUS_LABELS } from '../../shared/format';
import { Skeleton } from '../../shared/skeleton';
import { StatusBadge } from '../../shared/status-badge';

type Tab = 'unassigned' | 'me' | 'all';

@Component({
  selector: 'app-queue',
  imports: [FormsModule, RouterLink, StatusBadge, Skeleton],
  template: `
    <div class="page-head"><h1>Work queue</h1></div>

    <div class="tabs" role="tablist">
      <button role="tab" [attr.aria-selected]="tab() === 'unassigned'" [class.active]="tab() === 'unassigned'" (click)="setTab('unassigned')">Unassigned</button>
      <button role="tab" [attr.aria-selected]="tab() === 'me'" [class.active]="tab() === 'me'" (click)="setTab('me')">My claims</button>
      <button role="tab" [attr.aria-selected]="tab() === 'all'" [class.active]="tab() === 'all'" (click)="setTab('all')">All claims</button>
    </div>

    <div class="filters">
      <select aria-label="Status" [(ngModel)]="status" (ngModelChange)="load()">
        <option value="">Any status</option>
        @for (s of statuses; track s) { <option [value]="s">{{ labels[s] }}</option> }
      </select>
      <select aria-label="Market" [(ngModel)]="market" (ngModelChange)="load()">
        <option value="">All markets</option>
        @for (m of markets; track m) { <option [value]="m">{{ m }}</option> }
      </select>
      <input type="search" aria-label="Search claim number" placeholder="Search claim number" [(ngModel)]="search" (keyup.enter)="load()" (search)="load()" />
      <label class="check small">
        <input type="checkbox" [(ngModel)]="includeClosed" (ngModelChange)="load()" /> Include closed
      </label>
    </div>

    @if (error()) { <div class="error">{{ error() }}</div> }

    <div class="card table-wrap">
      <table>
        <thead>
          <tr>
            <th>Claim</th><th>Claimant</th><th>Type</th><th>Status</th>
            <th class="num">Claimed</th><th class="num">Reserve</th><th class="num">Age</th><th>Officer</th><th></th>
          </tr>
        </thead>
        <tbody>
          @for (c of claims(); track c.id) {
            <tr class="clickable" (click)="open(c)">
              <td><a class="row-link" [routerLink]="['/claims', c.id]" (click)="$event.stopPropagation()">{{ c.claimNumber }}</a></td>
              <td>{{ c.claimantName }}</td>
              <td>{{ c.type }} · {{ c.market }}</td>
              <td><app-status [status]="c.status" /> @if (c.openInfoRequests) { <span class="muted small">({{ c.openInfoRequests }} open)</span> }</td>
              <td class="num">{{ money(c.claimedAmount, c.currency) }}</td>
              <td class="num">{{ money(c.reserveAmount, c.currency) }}</td>
              <td class="num" [class.old]="c.ageDays > 14 && c.status !== 'Settled' && c.status !== 'Rejected'">{{ c.ageDays }}d</td>
              <td>{{ c.assignedOfficerName ?? '—' }}</td>
              <td>
                @if (!c.assignedOfficerId) {
                  <button class="btn small primary" (click)="pickUp(c, $event)" [disabled]="busy()">Pick up</button>
                }
              </td>
            </tr>
          } @empty {
            @if (loading()) {
              @for (i of [1, 2, 3]; track i) { <tr><td colspan="9"><app-skeleton [rows]="1" /></td></tr> }
            } @else {
              <tr><td colspan="9" class="empty-row">{{ emptyText() }}</td></tr>
            }
          }
        </tbody>
      </table>
    </div>
  `,
})
export class Queue {
  private api = inject(ApiService);
  private router = inject(Router);
  private notifications = inject(NotificationsService);

  protected statuses = ALL_STATUSES;
  protected labels = STATUS_LABELS;
  protected markets = MARKETS;
  protected money = money;

  protected tab = signal<Tab>(this.restoreTab());
  protected status: ClaimStatus | '' = '';
  protected market: Market | '' = '';
  protected search = '';
  protected includeClosed = false;

  protected claims = signal<ClaimSummary[]>([]);
  protected loading = signal(true);
  protected busy = signal(false);
  protected error = signal('');

  constructor() {
    effect(() => {
      this.notifications.changed();
      this.load();
    });
  }

  protected emptyText(): string {
    if (this.search || this.status || this.market) return 'No claims match these filters.';
    return this.tab() === 'unassigned' ? 'Nothing waiting to be picked up. The queue is clear.'
      : this.tab() === 'me' ? "You don't have any open claims. Pick one up from Unassigned."
      : 'No open claims.';
  }

  protected setTab(tab: Tab): void {
    this.tab.set(tab);
    try { sessionStorage.setItem('queue.tab', tab); } catch { /* ignore */ }
    this.load();
  }

  protected async load(): Promise<void> {
    const t = this.tab();
    const filter: ClaimFilter = {
      assigned: t === 'all' ? '' : t,
      status: this.status,
      market: this.market,
      search: this.search,
      openOnly: !this.includeClosed,
    };
    this.loading.set(true);
    try {
      this.claims.set(await this.api.claims(filter));
      this.error.set('');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected open(c: ClaimSummary): void {
    this.router.navigate(['/claims', c.id]);
  }

  protected async pickUp(c: ClaimSummary, event: Event): Promise<void> {
    event.stopPropagation();
    this.busy.set(true);
    try {
      await this.api.assign(c.id);
      this.router.navigate(['/claims', c.id]);
    } catch (e) {
      this.error.set(errorMessage(e));
      this.load();
    } finally {
      this.busy.set(false);
    }
  }

  private restoreTab(): Tab {
    try { return (sessionStorage.getItem('queue.tab') as Tab) || 'unassigned'; } catch { return 'unassigned'; }
  }
}
