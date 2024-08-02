﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore.DependencyInjection;
using Volo.Abp.EventBus.Local;
using Volo.Abp.TestApp.Domain;
using Volo.Abp.TestApp.Testing;
using Volo.Abp.Uow;
using Xunit;

namespace Volo.Abp.EntityFrameworkCore.DomainEvents;

public class DomainEvents_Tests : DomainEvents_Tests<AbpEntityFrameworkCoreTestModule>
{
}

public class AbpEntityChangeOptions_DomainEvents_Tests : AbpEntityChangeOptions_DomainEvents_Tests<AbpEntityFrameworkCoreTestModule>
{
}

public class AbpEfCoreDomainEvents_Tests : EntityFrameworkCoreTestBase
{
    protected readonly IRepository<AppEntityWithNavigations, Guid> AppEntityWithNavigationsRepository;
    protected readonly IRepository<Product, Guid> ProductRepository;
    protected readonly ILocalEventBus LocalEventBus;
    protected readonly IRepository<Person, Guid> PersonRepository;
    protected bool _loadEntityWithDetails = false;

    public AbpEfCoreDomainEvents_Tests()
    {
        ProductRepository = GetRequiredService<IRepository<Product, Guid>>();
        AppEntityWithNavigationsRepository = GetRequiredService<IRepository<AppEntityWithNavigations, Guid>>();
        LocalEventBus = GetRequiredService<ILocalEventBus>();
        PersonRepository = GetRequiredService<IRepository<Person, Guid>>();
    }

    protected override void AfterAddApplication(IServiceCollection services)
    {
        services.Configure<AbpEntityOptions>(options =>
        {
            options.Entity<AppEntityWithNavigations>(opt =>
            {
                if (_loadEntityWithDetails)
                {
                    opt.DefaultWithDetailsFunc = q => q;
                }
            });
        });

        base.AfterAddApplication(services);
    }

    [Fact]
    public async Task Should_Trigger_Domain_Events_For_Aggregate_Root_When_Navigation_Changes_Tests()
    {
        _loadEntityWithDetails = false;

        var entityId = Guid.NewGuid();

        await AppEntityWithNavigationsRepository.InsertAsync(new AppEntityWithNavigations(entityId, "TestEntity"));

        var entityUpdatedEventTriggered = false;
        var personCreatedEventCount = 0;
        var entityUpdatedEventTriggerCount = 0;

        LocalEventBus.Subscribe<EntityCreatedEventData<Person>>(_ =>
        {
            personCreatedEventCount++;
           return Task.CompletedTask;
        });

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigations>>(async _ =>
        {
            entityUpdatedEventTriggered = true;
            await PersonRepository.InsertAsync(new Person(Guid.NewGuid(), Guid.NewGuid().ToString(), new Random().Next(1, 100)));
        });

        // Test with simple property
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.Name = Guid.NewGuid().ToString();
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        // Test with value object
        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.AppEntityWithValueObjectAddress = new AppEntityWithValueObjectAddress("Turkey");
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.AppEntityWithValueObjectAddress.Country = "USA";
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithValueObjectAddress>>(_ =>
        {
            throw new Exception("Should not trigger this event");
        });

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.AppEntityWithValueObjectAddress = null;
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        // Test with one to one
        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToOne = new AppEntityWithNavigationChildOneToOne
            {
                ChildName = "ChildName",
                OneToOne = new AppEntityWithNavigationChildOneToOneAndOneToOne
                {
                    ChildName = "OneToOne-ChildName"
                }
            };
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        var oneToOneEntityUpdatedEventTriggered = false;
        var oneToOneAndOneToOneEntityUpdatedEventTriggered = false;

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigationChildOneToOne>>(async _ =>
        {
            oneToOneEntityUpdatedEventTriggered = true;
        });
        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigationChildOneToOneAndOneToOne>>(async _ =>
        {
            oneToOneAndOneToOneEntityUpdatedEventTriggered = true;
        });

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToOne.ChildName = "ChildName2";
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        oneToOneEntityUpdatedEventTriggered.ShouldBeTrue();
        oneToOneAndOneToOneEntityUpdatedEventTriggered.ShouldBeFalse();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        entityUpdatedEventTriggered = false;
        oneToOneEntityUpdatedEventTriggered = false;
        oneToOneAndOneToOneEntityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToOne.OneToOne.ChildName = "OneToOne-ChildName2";
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        oneToOneEntityUpdatedEventTriggered.ShouldBeTrue();
        oneToOneAndOneToOneEntityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigationChildOneToOne>>(_ =>
        {
            throw new Exception("Should not trigger this event");
        });

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToOne = null;
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        // Test with one to many
        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToMany = new List<AppEntityWithNavigationChildOneToMany>()
            {
                new AppEntityWithNavigationChildOneToMany
                {
                    AppEntityWithNavigationId = entity.Id,
                    ChildName = "ChildName1",
                    OneToMany = new List<AppEntityWithNavigationChildOneToManyAndOneToMany>()
                    {
                        new AppEntityWithNavigationChildOneToManyAndOneToMany()
                        {
                            ChildName = "OneToMany-ChildName1"
                        }
                    }
                }
            };
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        var oneToManyEntityUpdatedEventTriggered = false;
        var oneToManyAndOneToManyEntityUpdatedEventTriggered = false;

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigationChildOneToMany>>(async _ =>
        {
            oneToManyEntityUpdatedEventTriggered = true;
        });
        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigationChildOneToManyAndOneToMany>>(async _ =>
        {
            oneToManyAndOneToManyEntityUpdatedEventTriggered = true;
        });

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToMany[0].ChildName = "ChildName2";
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        oneToManyEntityUpdatedEventTriggered.ShouldBeTrue();
        oneToManyAndOneToManyEntityUpdatedEventTriggered.ShouldBeFalse();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        entityUpdatedEventTriggered = false;
        oneToManyEntityUpdatedEventTriggered = false;
        oneToManyAndOneToManyEntityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToMany[0].OneToMany[0].ChildName = "OneToMany-ChildName2";
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        oneToManyEntityUpdatedEventTriggered.ShouldBeTrue();
        oneToManyAndOneToManyEntityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigationChildOneToMany>>(_ =>
        {
            throw new Exception("Should not trigger this event");
        });

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.OneToMany.Clear();
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        // Test with many to many
        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.ManyToMany = new List<AppEntityWithNavigationChildManyToMany>()
            {
                new AppEntityWithNavigationChildManyToMany
                {
                    ChildName = "ChildName1"
                }
            };
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.ManyToMany[0].ChildName = "ChildName2";
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);

        entityUpdatedEventTriggered = false;
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
            entity.ManyToMany.Clear();
            await AppEntityWithNavigationsRepository.UpdateAsync(entity);
        });
        entityUpdatedEventTriggered.ShouldBeTrue();
        personCreatedEventCount.ShouldBe(++entityUpdatedEventTriggerCount);
    }

    [Fact]
    public async Task Should_Trigger_Domain_Events_For_Aggregate_Root_When_EnsureCollectionLoaded_Navigation_Changes_Tests()
    {
        _loadEntityWithDetails = true;

        var entityId = Guid.NewGuid();

        await AppEntityWithNavigationsRepository.InsertAsync(new AppEntityWithNavigations(entityId, "TestEntity")
        {
            OneToMany = new List<AppEntityWithNavigationChildOneToMany>()
            {
                new AppEntityWithNavigationChildOneToMany
                {
                    ChildName = "ChildName1"
                }
            }
        });

        var entityUpdatedEventTriggered = false;

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigations>>(data =>
        {
            entityUpdatedEventTriggered = !entityUpdatedEventTriggered;
            return Task.CompletedTask;
        });

        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using (var uow = uowManager.Begin())
            {
                var entity = await AppEntityWithNavigationsRepository.GetAsync(entityId);
                entity.OneToMany.ShouldBeNull();

                await AppEntityWithNavigationsRepository.EnsureCollectionLoadedAsync(entity, x => x.OneToMany);
                entity.OneToMany.ShouldNotBeNull();

                entity.OneToMany.Clear();
                await AppEntityWithNavigationsRepository.UpdateAsync(entity);

                await uow.CompleteAsync();
            }
        }

        entityUpdatedEventTriggered.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Trigger_Domain_Events_For_Aggregate_Root_When_OneToOne_Null_Changes_Tests()
    {
        _loadEntityWithDetails = true;

        var entityId = Guid.NewGuid();

        await AppEntityWithNavigationsRepository.InsertAsync(new AppEntityWithNavigations(entityId, "TestEntity")
        {
            OneToOne = new AppEntityWithNavigationChildOneToOne
            {
                ChildName = "ChildName1"
            }
        });
        var productId = Guid.NewGuid();
        await ProductRepository.InsertAsync(new Product(productId, "Tem", 0));

        var entityUpdatedEventTriggered = false;

        LocalEventBus.Subscribe<EntityUpdatedEventData<AppEntityWithNavigations>>(data =>
        {
            entityUpdatedEventTriggered = !entityUpdatedEventTriggered;
            return Task.CompletedTask;
        });

        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using (var uow = uowManager.Begin())
            {
                var entity2 = await ProductRepository.GetAsync(productId);
                // await ProductRepository.DeleteAsync(entity2, true); //If the Product is deleted before the AppEntityWithNavigations query, the unit test works
                var entity = await (await AppEntityWithNavigationsRepository.GetQueryableAsync())
                    .Where(s => s.Id == entityId)
                    .Include(s => s.OneToOne)
                    .SingleOrDefaultAsync();
                // await ProductRepository.DeleteAsync(entity2); //If you change autoSave to false, the unit test works
                await ProductRepository.DeleteAsync(entity2, true); //Only here will the unit test fail
                entity.OneToOne = null;

                await AppEntityWithNavigationsRepository.UpdateAsync(entity);

                await uow.CompleteAsync();
            }
        }

        entityUpdatedEventTriggered.ShouldBeTrue();
    }
}
