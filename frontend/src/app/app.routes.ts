import { Routes } from '@angular/router';
import { claimantGuard, staffGuard } from './core/guards';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/login').then(m => m.Login) },

  { path: 'my-claims', canActivate: [claimantGuard], loadComponent: () => import('./features/claimant/my-claims').then(m => m.MyClaims) },
  { path: 'my-claims/new', canActivate: [claimantGuard], loadComponent: () => import('./features/claimant/new-claim').then(m => m.NewClaim) },
  { path: 'my-claims/:id', canActivate: [claimantGuard], loadComponent: () => import('./features/claimant/claim-view').then(m => m.ClaimView) },


  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
