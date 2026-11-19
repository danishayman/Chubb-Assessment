import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, ClaimFilter, errorMessage } from '../../core/api.service';
import { ClaimStatus, ClaimSummary, Market } from '../../core/models';
import { NotificationsService } from '../../core/notifications.service';
import { ALL_STATUSES, MARKETS, money, STATUS_LABELS } from '../../shared/format';
import { StatusBadge } from '../../shared/status-badge';

type Tab = 'unassigned' | 'me' | 'all';

@Component({
  selector: 'app-queue',
  imports: [FormsModule, StatusBadge],
  template: `
    <div class="page-head"><h1>Work queue</h1></div>

    <div class="tabs">
      <button [class.active]="tab() === 'unassigned'" (click)="setTab('unassigned')">Unassigned</button>
      <button [class.active]="tab() === 'me'" (click)="setTab('me')">My claims</button>
      <button [class.active]="tab() === 'all'" (click)="setTab('all')">All claims</button>
    </div>

    <div class="filters">
      <select [(ngModel)]="status" (ngModelChange)="load()">
        <option value="">Any status</option>
        @for (s of statuses; track s) { <option [value]="s">{{ labels[s] }}</option> }
      </select>
      <select [(ngModel)]="market" (ngModelChange)="load()">
        <option value="">All markets</option>
        @for (m of markets; track m) { <option [value]="m">{{ m }}</option> }
      </select>
      <input placeholder="Search claim number" [(ngModel)]="search" (keyup.enter)="load()" />
      <label class="small" style="flex-direction: row; align-items: center; gap: 6px">
        <input type="checkbox" style="width: auto" [(ngModel)]="includeClosed" (ngModelChange)="load()" /> Include closed
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
              <td><strong>{{ c.claimNumber }}</strong></td>
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
            <tr><td colspan="9" class="muted">{{ loading() ? 'Loading…' : 'No claims match.' }}</td></tr>
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
