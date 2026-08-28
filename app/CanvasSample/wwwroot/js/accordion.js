// Ported verbatim (types removed) from the Astro template's accordion-item
// client script. Loaded as a module from AccordionItem, so repeated tags
// execute once; each item guards itself with data-initialized.
const borderClasses = {
  gray_200: 'border-gray-200',
  gray_300: 'border-gray-300',
  gray_400: 'border-gray-400',
  primary_200: 'border-primary-200',
  primary_300: 'border-primary-300',
};

document.querySelectorAll('[data-accordion-item]:not([data-initialized])').forEach((item) => {
  item.dataset.initialized = 'true';
  const group = item.closest('[data-accordion-group]');
  const variant = group?.dataset.variant || 'default';
  const borderColor = group?.dataset.borderColor || 'gray_200';
  const items = Array.from(group?.querySelectorAll('[data-accordion-item]') || []);
  const index = items.indexOf(item);
  const button = item.querySelector('[data-accordion-button]');
  const panel = item.querySelector('[data-accordion-content]');
  const body = item.querySelector('[data-accordion-content-body]');
  const borderedIcon = item.querySelector('[data-bordered-icon]');
  const plusIcon = item.querySelector('[data-plus-icon]');
  const minusIcon = item.querySelector('[data-minus-icon]');
  const chevron = item.querySelector('[data-chevron-icon]');
  let open = item.dataset.defaultOpen === 'true';

  if (variant === 'bordered') {
    item.classList.add('border', 'bg-white', borderClasses[borderColor]);
    if (index > 0) item.classList.add('-mt-px');
    if (index === 0) item.classList.add('rounded-t-lg');
    if (index === items.length - 1) item.classList.add('rounded-b-lg');
    button.classList.add('px-5', 'py-4', 'text-base', 'text-black', 'hover:text-primary-700');
    body.classList.add('px-5');
    borderedIcon.classList.remove('hidden');
    borderedIcon.classList.add('inline-flex');
    chevron.classList.add('hidden');
  } else if (variant === 'separated') {
    item.classList.add('rounded-xl', 'border', 'bg-white', borderClasses[borderColor]);
    button.classList.add('justify-between', 'px-4', 'py-4', 'text-base', 'text-black', 'hover:text-primary-700');
    body.classList.add('px-4');
  } else {
    button.classList.add('justify-between', 'py-4', 'text-base', 'text-black', 'hover:text-primary-700');
  }

  const render = () => {
    button.setAttribute('aria-expanded', String(open));
    panel.setAttribute('aria-hidden', String(!open));
    panel.toggleAttribute('inert', !open);
    panel.classList.toggle('grid-rows-[1fr]', open);
    panel.classList.toggle('grid-rows-[0fr]', !open);
    plusIcon.classList.toggle('hidden', open);
    minusIcon.classList.toggle('hidden', !open);
    chevron.classList.toggle('rotate-180', open);
    chevron.classList.toggle('rotate-0', !open);
  };

  button.addEventListener('click', () => {
    open = !open;
    render();
  });
  const checkHash = () => {
    if (item.id && window.location.hash === `#${item.id}`) {
      open = true;
      render();
      requestAnimationFrame(() => item.scrollIntoView({ behavior: 'smooth', block: 'center' }));
    }
  };
  window.addEventListener('hashchange', checkHash);
  render();
  checkHash();
});
