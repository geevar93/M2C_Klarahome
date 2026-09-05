import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DataAccessCatalog } from './data-access-catalog';

describe('DataAccessCatalog', () => {
  let component: DataAccessCatalog;
  let fixture: ComponentFixture<DataAccessCatalog>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DataAccessCatalog]
    }).compileComponents();

    fixture = TestBed.createComponent(DataAccessCatalog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
