import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UiPatterns } from './ui-patterns';

describe('UiPatterns', () => {
  let component: UiPatterns;
  let fixture: ComponentFixture<UiPatterns>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UiPatterns],
    }).compileComponents();

    fixture = TestBed.createComponent(UiPatterns);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
