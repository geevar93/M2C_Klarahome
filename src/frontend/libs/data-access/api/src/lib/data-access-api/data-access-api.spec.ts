import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DataAccessApi } from './data-access-api';

describe('DataAccessApi', () => {
  let component: DataAccessApi;
  let fixture: ComponentFixture<DataAccessApi>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DataAccessApi]
    }).compileComponents();

    fixture = TestBed.createComponent(DataAccessApi);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
