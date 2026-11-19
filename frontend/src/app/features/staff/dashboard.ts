import { Component, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { Exposure, Workload } from '../../core/models';
import { NotificationsService } from '../../core/notifications.service';
import { money, STATUS_LABELS } from '../../shared/format';
import { Skeleton } from '../../shared/skeleton';
import { StatusBadge } from '../../shared/status-badge';

@Component({
  selector: 'app-dashboard',
  imports: [StatusBadge, Skeleton],
  template: `
    <div class="page-head">
      <h1>Team dashboard</h1>
      <button class="btn small ghost" (click)="load()" [disabled]="loading()">{{ loading() ? 'Refreshing…' : 'Refresh' }}</button>
    </div>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }

    @if (!workload() && !error()) {
      <div class="card"><app-skeleton [rows]="1" [height]="40" /></div>
      <div class="grid-2"><div class="card"><app-skeleton [rows]="4" /></div><div class="card"><app-skeleton [rows]="4" /></div></div>
    }

    @if (workload(); as w) {
      <dl class="summary card">
        <div><dt>Open claims</dt><dd>{{ w.openClaims }}</dd></div>
        <div><dt>Waiting to be picked up</dt><dd>{{ w.unassigned }}
          @if (w.unassigned) { <span class="muted small">oldest <span [class.old]="w.oldestUnassignedDays > 2">{{ w.oldestUnassignedDays }}d</span></span> }
        </dd></div>
        <div><dt>Waiting on claimants</dt><dd>{{ awaiting() }}</dd></div>
        <div><dt>Older than 30 days</dt><dd [class.old]="over30() > 0">{{ over30() }}</dd></div>
      </dl>

      <div class="grid-2">
        <section class="card">
          <h2>Open claims by status</h2>
          <div class="bars">
            @for (s of w.byStatus; track s.status) {
              <div class="bar-row">
                <span>{{ labels[s.status] }}</span>
                <div class="bar-track"><div class="bar-fill" [style.width.%]="pct(s.count, w.openClaims)"></div></div>
                <span class="num">{{ s.count }}</span>
              </div>
            }
          </div>
        </section>
        <section class="card">
          <h2>Age of open claims</h2>
          <div class="bars">
            @for (b of w.ageing; track b.label) {
              <div class="bar-row">
                <span>{{ b.label }}</span>
                <div class="bar-track"><div class="bar-fill" [style.width.%]="pct(b.count, w.openClaims)"></div></div>
                <span class="num">{{ b.count }}</span>
              </div>
            }
          </div>
        </section>
      </div>

      <section class="card table-wrap">
        <h2>Officer workload</h2>
        <table>
          <thead>
            <tr><th>Officer</th><th class="num">Open</th><th class="num">Waiting on claimant</th><th class="num">Oldest open</th><th class="num">Closed (30d)</th><th class="num">Avg days to close</th></tr>
          </thead>
          <tbody>
            @for (o of w.officers; track o.officerId) {
              <tr>
                <td>{{ o.name }}</td>
                <td class="num">{{ o.open }}</td>
                <td class="num">{{ o.awaitingInformation }}</td>
                <td class="num" [class.old]="o.oldestOpenDays > 14">{{ o.oldestOpenDays }}d</td>
                <td class="num">{{ o.closedLast30Days }}</td>
                <td class="num">{{ o.avgDaysToClose ?? '—' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }

    @if (exposure(); as e) {
      <section class="card table-wrap">
        <h2>Outstanding liability (reserves on open claims)</h2>
        <p class="muted small">Shown per market in local currency. There's no FX conversion, so there's no single total.</p>
        <table>
          <thead><tr><th>Market</th><th>Type</th><th class="num">Open claims</th><th class="num">Claimed</th><th class="num">Reserved</th></tr></thead>
          <tbody>
            @for (l of e.lines; track l.market + l.type) {
              <tr>
                <td>{{ l.market }}</td><td>{{ l.type }}</td><td class="num">{{ l.openClaims }}</td>
                <td class="num">{{ money(l.claimed, l.currency) }}</td>
                <td class="num"><strong>{{ money(l.reserved, l.currency) }}</strong></td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="muted">No open claims.</td></tr>
            }
          </tbody>
        </table>
      </section>

      <section class="card table-wrap">
        <h2>Largest open claims by market</h2>
        <table>
          <thead><tr><th>Claim</th><th>Market</th><th>Status</th><th class="num">Reserve</th></tr></thead>
          <tbody>
            @for (c of e.largestOpenByMarket; track c.id) {
              <tr class="clickable" (click)="router.navigate(['/claims', c.id])">
                <td>{{ c.claimNumber }}</td><td>{{ c.market }}</td><td><app-status [status]="c.status" /></td>
                <td class="num">{{ money(c.reserveAmount, c.currency) }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
  `,
})
export class Dashboard {
  private api = inject(ApiService);
  private notifications = inject(NotificationsService);
  protected router = inject(Router);

  protected workload = signal<Workload | null>(null);
  protected exposure = signal<Exposure | null>(null);
  protected error = signal('');
  protected labels = STATUS_LABELS;
  protected money = money;

  protected loading = signal(false);
  protected over30 = computed(() => this.workload()?.ageing.find(b => b.label.startsWith('30'))?.count ?? 0);
  protected awaiting = computed(() => this.workload()?.byStatus.find(s => s.status === 'AwaitingInformation')?.count ?? 0);

  constructor() {
    effect(() => {
      this.notifications.changed();
      this.load();
    });
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [w, e] = await Promise.all([this.api.workload(), this.api.exposure()]);
      this.workload.set(w);
      this.exposure.set(e);
      this.error.set('');
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }

  protected pct(n: number, total: number): number {
    return total === 0 ? 0 : Math.round((n / total) * 100);
  }
}
