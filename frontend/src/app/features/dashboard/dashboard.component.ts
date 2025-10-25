import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, MatIconModule, RouterModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent {
}
