import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DataAccessAuth } from './data-access-auth';

describe('DataAccessAuth', () => {
  let component: DataAccessAuth;
  let fixture: ComponentFixture<DataAccessAuth>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DataAccessAuth],
    }).compileComponents();

    fixture = TestBed.createComponent(DataAccessAuth);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
