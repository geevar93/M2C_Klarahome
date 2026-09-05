import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UiPrimitives } from './ui-primitives';

describe('UiPrimitives', () => {
  let component: UiPrimitives;
  let fixture: ComponentFixture<UiPrimitives>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UiPrimitives]
    }).compileComponents();

    fixture = TestBed.createComponent(UiPrimitives);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
