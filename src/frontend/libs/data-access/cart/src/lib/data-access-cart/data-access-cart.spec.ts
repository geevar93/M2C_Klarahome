import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DataAccessCart } from './data-access-cart';

describe('DataAccessCart', () => {
  let component: DataAccessCart;
  let fixture: ComponentFixture<DataAccessCart>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DataAccessCart],
    }).compileComponents();

    fixture = TestBed.createComponent(DataAccessCart);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
