import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UiLayout } from './ui-layout';

describe('UiLayout', () => {
  let component: UiLayout;
  let fixture: ComponentFixture<UiLayout>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UiLayout],
    }).compileComponents();

    fixture = TestBed.createComponent(UiLayout);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
