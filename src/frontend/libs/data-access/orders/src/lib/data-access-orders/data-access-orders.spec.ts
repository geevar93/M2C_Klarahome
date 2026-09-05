import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DataAccessOrders } from './data-access-orders';

describe('DataAccessOrders', () => {
  let component: DataAccessOrders;
  let fixture: ComponentFixture<DataAccessOrders>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DataAccessOrders],
    }).compileComponents();

    fixture = TestBed.createComponent(DataAccessOrders);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
