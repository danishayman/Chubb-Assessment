import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LoginResponse, User } from './models';

const STORAGE_KEY = 'claims.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private session = signal<LoginResponse | null>(this.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly isStaff = computed(() => {
    const role = this.user()?.role;
    return role === 'ClaimsOfficer' || role === 'Manager';
  });
  readonly isManager = computed(() => this.user()?.role === 'Manager');

  async login(email: string, password: string): Promise<User> {
    const res = await firstValueFrom(this.http.post<LoginResponse>('/api/auth/login', { email, password }));
    this.session.set(res);
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(res)); } catch { /* private mode */ }
    return res.user;
  }

  logout(): void {
    this.session.set(null);
    try { localStorage.removeItem(STORAGE_KEY); } catch { /* ignore */ }
    this.router.navigate(['/login']);
  }

  homeFor(user: User | null = this.user()): string {
    if (!user) return '/login';
    return user.role === 'Claimant' ? '/my-claims' : '/queue';
  }

  private restore(): LoginResponse | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const s = JSON.parse(raw) as LoginResponse;
      return new Date(s.expiresAt) > new Date() ? s : null;
    } catch {
      return null;
    }
  }
}
