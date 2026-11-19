import { DatePipe } from '@angular/common';
import { Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { NotificationsService } from './core/notifications.service';
import { AppNotification } from './core/models';
import { Icon } from './shared/icon';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe, Icon],
  host: { '(document:click)': 'outside($event)', '(document:keydown.escape)': 'open.set(false)' },
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
            <div class="bell-wrap" #bell>
              <button class="bell" (click)="open.set(!open())" [attr.aria-expanded]="open()"
                      [attr.aria-label]="notifications.unread() ? notifications.unread() + ' unread notifications' : 'Notifications'">
                <app-icon name="bell" [size]="20" />
                @if (notifications.unread() > 0) { <span class="bell-count">{{ notifications.unread() > 9 ? '9+' : notifications.unread() }}</span> }
              </button>
              @if (open()) {
                <div class="dropdown">
                  <div class="dropdown-head">
                    <strong>Notifications</strong>
                    @if (notifications.unread() > 0) {
                      <button class="link" (click)="notifications.markAllRead()">Mark all read</button>
                    }
                  </div>
                  @for (n of notifications.items(); track n.id) {
                    <button class="note-item" [class.unread]="!n.read" (click)="go(n)">
                      <div>{{ n.message }}</div>
                      <div class="muted small">{{ n.createdAt | date: 'd MMM, HH:mm' }}</div>
                    </button>
                  } @empty {
                    <div class="muted pad small">Updates about your claims will show up here.</div>
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
  private bell = viewChild<ElementRef<HTMLElement>>('bell');

  constructor() {
    effect(() => (this.auth.user() ? this.notifications.start() : this.notifications.stop()));
  }

  protected outside(event: MouseEvent): void {
    if (this.open() && !this.bell()?.nativeElement.contains(event.target as Node)) this.open.set(false);
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
