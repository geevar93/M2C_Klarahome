import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DataAccessContent } from './data-access-content';

describe('DataAccessContent', () => {
  let component: DataAccessContent;
  let fixture: ComponentFixture<DataAccessContent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DataAccessContent],
    }).compileComponents();

    fixture = TestBed.createComponent(DataAccessContent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
