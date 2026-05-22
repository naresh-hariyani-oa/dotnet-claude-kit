# AutoMapper Patterns

> Reference for projects using AutoMapper 14 for DTO ↔ entity mapping.
> Used in: `oneadvancedlegal-pcms-api` (all 30+ business domains).

---

## Profile Organization

One `Profile` class per business domain. Place it alongside the domain's DTOs.

```csharp
// Accounts/AccountProfile.cs
public class AccountProfile : Profile
{
    public AccountProfile()
    {
        CreateMap<Account, AccountDto>().ReverseMap();
        CreateMap<Account, AccountSummaryDto>();
        CreateMap<CreateAccountRequest, Account>();
    }
}
```

**Rules:**
- One profile per domain — never merge unrelated domains into a single profile.
- Name it `{Domain}Profile`.
- Always place `CreateMap` in the constructor, never elsewhere.
- Register all profiles at startup via `AddAutoMapper(Assembly.GetExecutingAssembly())` — do not manually register individual profiles.

---

## Bidirectional Mapping

Use `.ReverseMap()` when you need both `Entity → Dto` and `Dto → Entity`:

```csharp
CreateMap<Order, OrderDto>().ReverseMap();
// Generates both:
// mapper.Map<OrderDto>(order)
// mapper.Map<Order>(orderDto)
```

**When NOT to use `.ReverseMap()`:**
- When the reverse mapping has different logic (use two explicit `CreateMap` calls)
- For create/update request DTOs mapping to entities (use a dedicated `CreateMap<CreateRequest, Entity>()`)

---

## Custom Member Mapping

```csharp
CreateMap<Order, OrderDto>()
    .ForMember(dest => dest.CustomerFullName,
               opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}"))
    .ForMember(dest => dest.TotalWithTax,
               opt => opt.MapFrom(src => src.Total * 1.2m))
    .ForMember(dest => dest.InternalNotes,
               opt => opt.Ignore()); // Exclude from mapping
```

---

## Injecting IMapper

Inject `IMapper` via constructor. Never use the static `Mapper.Map<T>()` API.

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly IMapper _mapper;

    public OrderService(IOrderRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<OrderDto> GetOrderAsync(int id)
    {
        var order = await _repository.GetByIdAsync(id)
            ?? throw new ResourceNotFoundException($"Order {id} not found");
        return _mapper.Map<OrderDto>(order);
    }
}
```

---

## Safe Extension Points

**Safe to add:** New `CreateMap` lines inside an existing profile's constructor for new DTOs.

**Safe to add:** A new `{Domain}Profile.cs` file for a new business domain.

**High risk — do not modify casually:**
- Removing or changing an existing `CreateMap` — downstream code that calls `_mapper.Map<T>()` will throw `AutoMapperMappingException` at runtime if the map disappears.
- Renaming a DTO property without updating the profile — mapping will silently produce `null`/default values for the renamed property.
- Changing `.ReverseMap()` to a unidirectional map — breaks callers that use the reverse direction.

---

## Common Pitfalls

### Pitfall: Duplicate profile registration

**Problem:** Defining the same `CreateMap<A, B>` in two different profile classes. AutoMapper will throw an `InvalidOperationException` at startup.

**Fix:** Search for existing maps before adding a new one:
```bash
grep -r "CreateMap<OrderDto" --include="*.cs"
```

### Pitfall: Missing `.ReverseMap()` for bidirectional operations

**Problem:** Controller or service calls `_mapper.Map<Entity>(dto)` but only `CreateMap<Entity, Dto>()` was defined (not `.ReverseMap()`). Throws `AutoMapperMappingException`.

**Fix:** Always add `.ReverseMap()` when the entity needs to be updated from a DTO.

### Pitfall: Mapping configuration validation failures at startup

AutoMapper validates all maps at startup in development. If a property exists on the destination but has no source mapping and no `.Ignore()`, it throws. Fix: either add a source mapping or explicitly `.Ignore()` the property.

---

## Decision Guide

| Scenario | Approach |
|---|---|
| New DTO for existing domain | Add `CreateMap` to existing `{Domain}Profile` |
| New business domain | Create `{Domain}Profile.cs` with all maps for that domain |
| Entity → DTO only | `CreateMap<Entity, Dto>()` (no ReverseMap) |
| Entity ↔ DTO bidirectional | `CreateMap<Entity, Dto>().ReverseMap()` |
| Create request → Entity | `CreateMap<CreateRequest, Entity>()` (separate map) |
| Property needs transformation | `.ForMember(dest => ..., opt => opt.MapFrom(...))` |
| Property should not be mapped | `.ForMember(dest => ..., opt => opt.Ignore())` |
