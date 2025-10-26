import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse, RefreshTokenRequest, RefreshTokenResponse, InitialSetupRequest, NeedsSetupResponse } from '../models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5075/api';

  private currentUserSubject = new BehaviorSubject<string | null>(this.getStoredUsername());
  public currentUser$ = this.currentUserSubject.asObservable();

  private isAuthenticatedSubject = new BehaviorSubject<boolean>(this.hasValidToken());
  public isAuthenticated$ = this.isAuthenticatedSubject.asObservable();

  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/auth/login`, credentials)
      .pipe(
        tap(response => {
          this.storeTokens(response);
          this.currentUserSubject.next(response.username);
          this.isAuthenticatedSubject.next(true);
        })
      );
  }

  logout(): Observable<any> {
    const refreshToken = this.getRefreshToken();

    return this.http.post(`${this.apiUrl}/auth/logout`, { refreshToken })
      .pipe(
        tap(() => {
          this.clearTokens();
          this.currentUserSubject.next(null);
          this.isAuthenticatedSubject.next(false);
        })
      );
  }

  refreshToken(): Observable<RefreshTokenResponse> {
    const refreshToken = this.getRefreshToken();

    if (!refreshToken) {
      throw new Error('No refresh token available');
    }

    const request: RefreshTokenRequest = { refreshToken };

    return this.http.post<RefreshTokenResponse>(`${this.apiUrl}/auth/refresh`, request)
      .pipe(
        tap(response => {
          this.storeTokens(response);
        })
      );
  }

  getAccessToken(): string | null {
    return localStorage.getItem('accessToken');
  }

  getRefreshToken(): string | null {
    return localStorage.getItem('refreshToken');
  }

  isAuthenticated(): boolean {
    return this.hasValidToken();
  }

  private storeTokens(response: LoginResponse | RefreshTokenResponse): void {
    localStorage.setItem('accessToken', response.accessToken);
    localStorage.setItem('refreshToken', response.refreshToken);
    localStorage.setItem('tokenExpiry', response.expiresAt);

    if ('username' in response) {
      localStorage.setItem('username', response.username);
    }
  }

  private clearTokens(): void {
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('tokenExpiry');
    localStorage.removeItem('username');
  }

  private getStoredUsername(): string | null {
    return localStorage.getItem('username');
  }

  private hasValidToken(): boolean {
    const token = this.getAccessToken();
    const expiry = localStorage.getItem('tokenExpiry');

    if (!token || !expiry) {
      return false;
    }

    const expiryDate = new Date(expiry);
    return expiryDate > new Date();
  }

  needsSetup(): Observable<NeedsSetupResponse> {
    return this.http.get<NeedsSetupResponse>(`${this.apiUrl}/auth/needs-setup`);
  }

  initialSetup(request: InitialSetupRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/auth/setup`, request)
      .pipe(
        tap(response => {
          this.storeTokens(response);
          this.currentUserSubject.next(response.username);
          this.isAuthenticatedSubject.next(true);
        })
      );
  }
}
