import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { NotificationsService } from './core/notifications.service';
import { AppNotification } from './core/models';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe],
  template: `
    @if (auth.user(); as user) {
      <header class="topbar">
        <div class="topbar-inner">
          <a class="brand" [routerLink]="auth.homeFor()">Claims<span>Portal</span></a>
          <nav>
            @if (auth.isStaff()) {
              <a routerLink="/queue" routerLinkActive="active">Work queue</a>
              <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
            } @else {
              <a routerLink="/my-claims" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">My claims</a>
              <a routerLink="/my-claims/new" routerLinkActive="active">Report an incident</a>
            }
          </nav>
          <div class="topbar-right">
            <div class="bell-wrap">
              <button class="bell" (click)="open.set(!open())" [attr.aria-expanded]="open()" aria-label="Notifications">
                🔔 @if (notifications.unread() > 0) { <span class="bell-count">{{ notifications.unread() }}</span> }
              </button>
              @if (open()) {
                <div class="dropdown">
                  <div class="dropdown-head">
                    <strong>Notifications</strong>
                    <button class="link" (click)="notifications.markAllRead()">Mark all read</button>
                  </div>
                  @for (n of notifications.items(); track n.id) {
                    <button class="note-item" [class.unread]="!n.read" (click)="go(n)">
                      <div>{{ n.message }}</div>
                      <div class="muted small">{{ n.createdAt | date: 'd MMM, HH:mm' }}</div>
                    </button>
                  } @empty {
                    <div class="muted pad">Nothing yet.</div>
                  }
                </div>
              }
            </div>
            <span class="who">{{ user.name }} <span class="muted small">· {{ roleLabel(user.role) }}</span></span>
            <button class="btn ghost small" (click)="logout()">Sign out</button>
          </div>
        </div>
      </header>
    }
    <main class="page"><router-outlet /></main>
  `,
})
export class App {
  protected auth = inject(AuthService);
  protected notifications = inject(NotificationsService);
  private router = inject(Router);
  protected open = signal(false);

  constructor() {
    effect(() => (this.auth.user() ? this.notifications.start() : this.notifications.stop()));
  }

  protected go(n: AppNotification): void {
    this.open.set(false);
    if (!n.read) this.notifications.markRead(n.id);
    this.router.navigate([this.auth.isStaff() ? '/claims' : '/my-claims', n.claimId]);
  }

  protected roleLabel(role: string): string {
    return role === 'ClaimsOfficer' ? 'Claims officer' : role;
  }

  protected logout(): void {
    this.open.set(false);
    this.auth.logout();
  }
}
