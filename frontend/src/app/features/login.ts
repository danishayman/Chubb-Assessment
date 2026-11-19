import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { errorMessage } from '../core/api.service';

const DEMO = [
  { email: 'aisha@example.com', label: 'Aisha Tan', role: 'Claimant (SG)' },
  { email: 'raj@example.com', label: 'Raj Kumar', role: 'Claimant (MY)' },
  { email: 'daniel@chubb.test', label: 'Daniel Lim', role: 'Claims officer' },
  { email: 'priya@chubb.test', label: 'Priya Nair', role: 'Claims officer' },
  { email: 'sarah@chubb.test', label: 'Sarah Chen', role: 'Manager' },
];

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="login">
      <div class="card login-card">
        <h1>Claims<span class="accent">Portal</span></h1>
        <p class="muted">Report and track motor and property claims.</p>

        <form (ngSubmit)="submit()" class="stack">
          <label>Email <input name="email" type="email" [(ngModel)]="email" required autocomplete="username" /></label>
          <label>Password <input name="password" type="password" [(ngModel)]="password" required autocomplete="current-password" /></label>
          @if (error()) { <div class="error">{{ error() }}</div> }
          <button class="btn primary" type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
        </form>

        <div class="demo">
          <div class="muted small">Demo accounts (password <code>Password123!</code>)</div>
          <div class="demo-grid">
            @for (d of demo; track d.email) {
              <button class="demo-btn" type="button" (click)="useDemo(d.email)">
                <strong>{{ d.label }}</strong><span class="muted small">{{ d.role }}</span>
              </button>
            }
          </div>
        </div>
      </div>
    </div>
  `,
})
export class Login {
  private auth = inject(AuthService);
  private router = inject(Router);

  protected demo = DEMO;
  protected email = '';
  protected password = '';
  protected busy = signal(false);
  protected error = signal('');

  constructor() {
    if (this.auth.user()) this.router.navigateByUrl(this.auth.homeFor());
  }

  protected useDemo(email: string): void {
    this.email = email;
    this.password = 'Password123!';
    this.submit();
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const user = await this.auth.login(this.email, this.password);
      this.router.navigateByUrl(this.auth.homeFor(user));
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
