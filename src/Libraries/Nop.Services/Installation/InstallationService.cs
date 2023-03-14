using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Core.Domain;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Cms;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Forums;
using Nop.Core.Domain.Gdpr;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Logging;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.News;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Seo;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Tax;
using Nop.Core.Domain.Topics;
using Nop.Core.Domain.Vendors;
using Nop.Core.Http;
using Nop.Core.Infrastructure;
using Nop.Core.Security;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.ExportImport;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Seo;

namespace Nop.Services.Installation
{
    /// <summary>
    /// Installation service
    /// </summary>
    public partial class InstallationService : IInstallationService
    {
        #region Fields

        private readonly INopDataProvider _dataProvider;
        private readonly INopFileProvider _fileProvider;
        private readonly IRepository<Country> _countryRepository;
        private readonly IRepository<Currency> _currencyRepository;
        private readonly IRepository<EmailAccount> _emailAccountRepository;
        private readonly IRepository<Language> _languageRepository;
        private readonly IRepository<MeasureDimension> _measureDimensionRepository;
        private readonly IRepository<MeasureWeight> _measureWeightRepository;
        private readonly IRepository<StateProvince> _stateProvinceRepository;
        private readonly IRepository<Store> _storeRepository;
        private readonly IRepository<TopicTemplate> _topicTemplateRepository;
        private readonly IRepository<UrlRecord> _urlRecordRepository;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public InstallationService(INopDataProvider dataProvider,
            INopFileProvider fileProvider,
            IRepository<Country> countryRepository,
            IRepository<Currency> currencyRepository,
            IRepository<EmailAccount> emailAccountRepository,
            IRepository<Language> languageRepository,
            IRepository<MeasureDimension> measureDimensionRepository,
            IRepository<MeasureWeight> measureWeightRepository,
            IRepository<StateProvince> stateProvinceRepository,
            IRepository<Store> storeRepository,
            IRepository<TopicTemplate> topicTemplateRepository,
            IRepository<UrlRecord> urlRecordRepository,
            IWebHelper webHelper)
        {
            _dataProvider = dataProvider;
            _fileProvider = fileProvider;
            _countryRepository = countryRepository;
            _currencyRepository = currencyRepository;
            _emailAccountRepository = emailAccountRepository;
            _languageRepository = languageRepository;
            _measureDimensionRepository = measureDimensionRepository;
            _measureWeightRepository = measureWeightRepository;
            _stateProvinceRepository = stateProvinceRepository;
            _storeRepository = storeRepository;
            _topicTemplateRepository = topicTemplateRepository;
            _urlRecordRepository = urlRecordRepository;
            _webHelper = webHelper;
        }

        #endregion

        #region Utilities

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task<T> InsertInstallationDataAsync<T>(T entity) where T : BaseEntity
        {
            return await _dataProvider.InsertEntityAsync(entity);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InsertInstallationDataAsync<T>(params T[] entities) where T : BaseEntity
        {
            await _dataProvider.BulkInsertEntitiesAsync(entities);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InsertInstallationDataAsync<T>(IList<T> entities) where T : BaseEntity
        {
            if (!entities.Any())
                return;

            await InsertInstallationDataAsync(entities.ToArray());
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task UpdateInstallationDataAsync<T>(T entity) where T : BaseEntity
        {
            await _dataProvider.UpdateEntityAsync(entity);
        }
        
        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task<string> ValidateSeNameAsync<T>(T entity, string seName) where T : BaseEntity
        {
            //duplicate of ValidateSeName method of \Nop.Services\Seo\UrlRecordService.cs (we cannot inject it here)
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            //validation
            var okChars = "abcdefghijklmnopqrstuvwxyz1234567890 _-";
            seName = seName.Trim().ToLowerInvariant();

            var sb = new StringBuilder();
            foreach (var c in seName.ToCharArray())
            {
                var c2 = c.ToString();
                if (okChars.Contains(c2))
                    sb.Append(c2);
            }

            seName = sb.ToString();
            seName = seName.Replace(" ", "-");
            while (seName.Contains("--"))
                seName = seName.Replace("--", "-");
            while (seName.Contains("__"))
                seName = seName.Replace("__", "_");

            //max length
            seName = CommonHelper.EnsureMaximumLength(seName, NopSeoDefaults.SearchEngineNameLength);

            //ensure this sename is not reserved yet
            var i = 2;
            var tempSeName = seName;
            while (true)
            {
                //check whether such slug already exists (and that is not the current entity)

                var query = from ur in _urlRecordRepository.Table
                            where tempSeName != null && ur.Slug == tempSeName
                            select ur;
                var urlRecord = await query.FirstOrDefaultAsync();

                var entityName = entity.GetType().Name;
                var reserved = urlRecord != null && !(urlRecord.EntityId == entity.Id && urlRecord.EntityName.Equals(entityName, StringComparison.InvariantCultureIgnoreCase));
                if (!reserved)
                    break;

                tempSeName = $"{seName}-{i}";
                i++;
            }

            seName = tempSeName;

            return seName;
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallStoresAsync()
        {
            var storeUrl = _webHelper.GetStoreLocation();
            var stores = new List<Store>
            {
                new Store
                {
                    Name = "Your store name",
                    Url = storeUrl,
                    SslEnabled = _webHelper.IsCurrentConnectionSecured(),
                    Hosts = "yourstore.com,www.yourstore.com",
                    DisplayOrder = 1,
                    //should we set some default company info?
                    CompanyName = "Your company name",
                    CompanyAddress = "your company country, state, zip, street, etc",
                    CompanyPhoneNumber = "(123) 456-78901",
                    CompanyVat = null
                }
            };

            await InsertInstallationDataAsync(stores);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallMeasuresAsync(RegionInfo regionInfo)
        {
            var isMetric = regionInfo?.IsMetric ?? false;

            var measureDimensions = new List<MeasureDimension>
            {
                new MeasureDimension
                {
                    Name = "inch(es)",
                    SystemKeyword = "inches",
                    Ratio = isMetric ? 39.3701M : 1M,
                    DisplayOrder = isMetric ? 1 : 0
                },
                new MeasureDimension
                {
                    Name = "feet",
                    SystemKeyword = "feet",
                    Ratio = isMetric ? 3.28084M : 0.08333333M,
                    DisplayOrder = isMetric ? 1 : 0
                },
                new MeasureDimension
                {
                    Name = "meter(s)",
                    SystemKeyword = "meters",
                    Ratio = isMetric ? 1M : 0.0254M,
                    DisplayOrder = isMetric ? 0 : 1
                },
                new MeasureDimension
                {
                    Name = "millimetre(s)",
                    SystemKeyword = "millimetres",
                    Ratio = isMetric ? 1000M : 25.4M,
                    DisplayOrder = isMetric ? 0 : 1
                }
            };

            await InsertInstallationDataAsync(measureDimensions);

            var measureWeights = new List<MeasureWeight>
            {
                new MeasureWeight
                {
                    Name = "ounce(s)",
                    SystemKeyword = "ounce",
                    Ratio = isMetric ? 35.274M : 16M,
                    DisplayOrder = isMetric ? 1 : 0
                },
                new MeasureWeight
                {
                    Name = "lb(s)",
                    SystemKeyword = "lb",
                    Ratio = isMetric ? 2.20462M : 1M,
                    DisplayOrder = isMetric ? 1 : 0
                },
                new MeasureWeight
                {
                    Name = "kg(s)",
                    SystemKeyword = "kg",
                    Ratio = isMetric ? 1M : 0.45359237M,
                    DisplayOrder = isMetric ? 0 : 1
                },
                new MeasureWeight
                {
                    Name = "gram(s)",
                    SystemKeyword = "grams",
                    Ratio = isMetric ? 1000M : 453.59237M,
                    DisplayOrder = isMetric ? 0 : 1
                }
            };

            await InsertInstallationDataAsync(measureWeights);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallTaxCategoriesAsync()
        {
            var taxCategories = new List<TaxCategory>
            {
                new TaxCategory {Name = "VAT", DisplayOrder = 1},
            };

            await InsertInstallationDataAsync(taxCategories);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallLanguagesAsync((string languagePackDownloadLink, int languagePackProgress) languagePackInfo, CultureInfo cultureInfo, RegionInfo regionInfo)
        {
            var localizationService = EngineContext.Current.Resolve<ILocalizationService>();

            var defaultCulture = new CultureInfo(NopCommonDefaults.DefaultLanguageCulture);
            var defaultLanguage = new Language
            {
                Name = defaultCulture.TwoLetterISOLanguageName.ToUpperInvariant(),
                LanguageCulture = defaultCulture.Name,
                UniqueSeoCode = defaultCulture.TwoLetterISOLanguageName,
                FlagImageFileName = $"{defaultCulture.Name.ToLowerInvariant()[^2..]}.png",
                Rtl = defaultCulture.TextInfo.IsRightToLeft,
                Published = true,
                DisplayOrder = 1
            };
            await InsertInstallationDataAsync(defaultLanguage);

            //Install locale resources for default culture
            var directoryPath = _fileProvider.MapPath(NopInstallationDefaults.LocalizationResourcesPath);
            var pattern = $"*.{NopInstallationDefaults.LocalizationResourcesFileExtension}";
            foreach (var filePath in _fileProvider.EnumerateFiles(directoryPath, pattern))
            {
                using var streamReader = new StreamReader(filePath);
                await localizationService.ImportResourcesFromXmlAsync(defaultLanguage, streamReader);
            }

            if (cultureInfo == null || regionInfo == null || cultureInfo.Name == NopCommonDefaults.DefaultLanguageCulture)
                return;

            var language = new Language
            {
                Name = cultureInfo.TwoLetterISOLanguageName.ToUpperInvariant(),
                LanguageCulture = cultureInfo.Name,
                UniqueSeoCode = cultureInfo.TwoLetterISOLanguageName,
                FlagImageFileName = $"{regionInfo.TwoLetterISORegionName.ToLowerInvariant()}.png",
                Rtl = cultureInfo.TextInfo.IsRightToLeft,
                Published = true,
                DisplayOrder = 2
            };
            await InsertInstallationDataAsync(language);

            if (string.IsNullOrEmpty(languagePackInfo.languagePackDownloadLink))
                return;

            //download and import language pack
            try
            {
                var httpClientFactory = EngineContext.Current.Resolve<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(NopHttpDefaults.DefaultHttpClient);
                await using var stream = await httpClient.GetStreamAsync(languagePackInfo.languagePackDownloadLink);
                using var streamReader = new StreamReader(stream);
                await localizationService.ImportResourcesFromXmlAsync(language, streamReader);

                //set this language as default
                language.DisplayOrder = 0;
                await UpdateInstallationDataAsync(language);

                //save progress for showing in admin panel (only for first start)
                await InsertInstallationDataAsync(new GenericAttribute
                {
                    EntityId = language.Id,
                    Key = NopCommonDefaults.LanguagePackProgressAttribute,
                    KeyGroup = nameof(Language),
                    Value = languagePackInfo.languagePackProgress.ToString(),
                    StoreId = 0,
                    CreatedOrUpdatedDateUTC = DateTime.UtcNow
                });
            }
            catch { }
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallCurrenciesAsync(CultureInfo cultureInfo, RegionInfo regionInfo)
        {
            //set some currencies with a rate against the USD
            var defaultCurrencies = new List<string>() { "USD", "AUD", "GBP", "CAD", "CNY", "EUR", "HKD", "JPY", "RUB", "SEK", "INR" };
            var currencies = new List<Currency>
            {
                // new Currency
                // {
                //     Name = "US Dollar",
                //     CurrencyCode = "USD",
                //     Rate = 1,
                //     DisplayLocale = "en-US",
                //     CustomFormatting = string.Empty,
                //     Published = true,
                //     DisplayOrder = 1,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Australian Dollar",
                //     CurrencyCode = "AUD",
                //     Rate = 1.34M,
                //     DisplayLocale = "en-AU",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 2,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "British Pound",
                //     CurrencyCode = "GBP",
                //     Rate = 0.75M,
                //     DisplayLocale = "en-GB",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 3,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Canadian Dollar",
                //     CurrencyCode = "CAD",
                //     Rate = 1.32M,
                //     DisplayLocale = "en-CA",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 4,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Chinese Yuan Renminbi",
                //     CurrencyCode = "CNY",
                //     Rate = 6.43M,
                //     DisplayLocale = "zh-CN",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 5,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Euro",
                //     CurrencyCode = "EUR",
                //     Rate = 0.86M,
                //     DisplayLocale = string.Empty,
                //     CustomFormatting = $"{"\u20ac"}0.00", //euro symbol
                //     Published = false,
                //     DisplayOrder = 6,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Hong Kong Dollar",
                //     CurrencyCode = "HKD",
                //     Rate = 7.84M,
                //     DisplayLocale = "zh-HK",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 7,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Japanese Yen",
                //     CurrencyCode = "JPY",
                //     Rate = 110.45M,
                //     DisplayLocale = "ja-JP",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 8,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Russian Rouble",
                //     CurrencyCode = "RUB",
                //     Rate = 63.25M,
                //     DisplayLocale = "ru-RU",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 9,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // },
                // new Currency
                // {
                //     Name = "Swedish Krona",
                //     CurrencyCode = "SEK",
                //     Rate = 8.80M,
                //     DisplayLocale = "sv-SE",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 10,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding1
                // },
                // new Currency
                // {
                //     Name = "Indian Rupee",
                //     CurrencyCode = "INR",
                //     Rate = 68.03M,
                //     DisplayLocale = "en-IN",
                //     CustomFormatting = string.Empty,
                //     Published = false,
                //     DisplayOrder = 12,
                //     CreatedOnUtc = DateTime.UtcNow,
                //     UpdatedOnUtc = DateTime.UtcNow,
                //     RoundingType = RoundingType.Rounding001
                // }
            };

            //set additional currency
            if (cultureInfo != null && regionInfo != null)
            {
                // if (!defaultCurrencies.Contains(regionInfo.ISOCurrencySymbol))
                // {
                // تومان
                    currencies.Add(new Currency
                    {
                        Name = "تومان",
                        CurrencyCode = regionInfo.ISOCurrencySymbol,
                        Rate = 1,
                        DisplayLocale = cultureInfo.Name,
                        CustomFormatting = string.Empty,
                        Published = true,
                        DisplayOrder = 0,
                        CreatedOnUtc = DateTime.UtcNow,
                        UpdatedOnUtc = DateTime.UtcNow,
                        RoundingType = RoundingType.Rounding001
                    });
                // }

                // foreach (var currency in currencies.Where(currency => currency.CurrencyCode == regionInfo.ISOCurrencySymbol))
                // {
                //     currency.Published = true;
                //     currency.DisplayOrder = 0;
                // }
            }


            await InsertInstallationDataAsync(currencies);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallCountriesAndStatesAsync()
        {
            var countries = ISO3166.GetCollection()
                .Where(country => country.Alpha3 == "IRN")
                .Select(country => new Country
                {
                    Name = "ایران",
                    AllowsBilling = true,
                    AllowsShipping = true,
                    TwoLetterIsoCode = country.Alpha2,
                    ThreeLetterIsoCode = country.Alpha3,
                    NumericIsoCode = country.NumericCode,
                    SubjectToVat = country.SubjectToVat,
                    DisplayOrder = 1,
                    Published = true
                }).ToList();

            await InsertInstallationDataAsync(countries.ToArray());

            //Import states for all countries
            var directoryPath = _fileProvider.MapPath(NopInstallationDefaults.LocalizationResourcesPath);
            var pattern = "*.txt";

            //we use different scope to prevent creating wrong settings in DI, because the settings data not exists yet
            var serviceScopeFactory = EngineContext.Current.Resolve<IServiceScopeFactory>();
            using var scope = serviceScopeFactory.CreateScope();
            var importManager = EngineContext.Current.Resolve<IImportManager>(scope);
            foreach (var filePath in _fileProvider.EnumerateFiles(directoryPath, pattern))
            {
                await using var stream = new FileStream(filePath, FileMode.Open);
                await importManager.ImportStatesFromTxtAsync(stream, false);
            }
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallCustomersAndUsersAsync(string defaultUserEmail, string defaultUserPassword)
        {
            var crAdministrators = new CustomerRole
            {
                Name = "Administrators",
                Active = true,
                IsSystemRole = true,
                SystemName = NopCustomerDefaults.AdministratorsRoleName
            };
            var crForumModerators = new CustomerRole
            {
                Name = "Forum Moderators",
                Active = true,
                IsSystemRole = true,
                SystemName = NopCustomerDefaults.ForumModeratorsRoleName
            };
            var crRegistered = new CustomerRole
            {
                Name = "Registered",
                Active = true,
                IsSystemRole = true,
                SystemName = NopCustomerDefaults.RegisteredRoleName
            };
            var crGuests = new CustomerRole
            {
                Name = "Guests",
                Active = true,
                IsSystemRole = true,
                SystemName = NopCustomerDefaults.GuestsRoleName
            };
            var crVendors = new CustomerRole
            {
                Name = "Vendors",
                Active = true,
                IsSystemRole = true,
                SystemName = NopCustomerDefaults.VendorsRoleName
            };
            var customerRoles = new List<CustomerRole>
            {
                crAdministrators,
                crForumModerators,
                crRegistered,
                crGuests,
                crVendors
            };

            await InsertInstallationDataAsync(customerRoles);

            //default store 
            var defaultStore = await _storeRepository.Table.FirstOrDefaultAsync();

            if (defaultStore == null)
                throw new Exception("No default store could be loaded");

            var storeId = defaultStore.Id;

            //admin user
            var adminUser = new Customer
            {
                CustomerGuid = Guid.NewGuid(),
                Email = defaultUserEmail,
                Username = defaultUserEmail,
                Active = true,
                CreatedOnUtc = DateTime.UtcNow,
                LastActivityDateUtc = DateTime.UtcNow,
                RegisteredInStoreId = storeId
            };

            var defaultAdminUserAddress = await InsertInstallationDataAsync(
                new Address
                {
                    FirstName = "John",
                    LastName = "Smith",
                    PhoneNumber = "12345678",
                    Email = defaultUserEmail,
                    FaxNumber = string.Empty,
                    Company = "Nop Solutions Ltd",
                    Address1 = "21 West 52nd Street",
                    Address2 = string.Empty,
                    City = "New York",
                    StateProvinceId = _stateProvinceRepository.Table.FirstOrDefault(sp => sp.Name == "New York")?.Id,
                    CountryId = _countryRepository.Table.FirstOrDefault(c => c.ThreeLetterIsoCode == "USA")?.Id,
                    ZipPostalCode = "10021",
                    CreatedOnUtc = DateTime.UtcNow
                });

            adminUser.BillingAddressId = defaultAdminUserAddress.Id;
            adminUser.ShippingAddressId = defaultAdminUserAddress.Id;

            await InsertInstallationDataAsync(adminUser);

            await InsertInstallationDataAsync(new CustomerAddressMapping { CustomerId = adminUser.Id, AddressId = defaultAdminUserAddress.Id });

            await InsertInstallationDataAsync(
                new CustomerCustomerRoleMapping { CustomerId = adminUser.Id, CustomerRoleId = crAdministrators.Id },
                new CustomerCustomerRoleMapping { CustomerId = adminUser.Id, CustomerRoleId = crForumModerators.Id },
                new CustomerCustomerRoleMapping { CustomerId = adminUser.Id, CustomerRoleId = crRegistered.Id });

            //set hashed admin password
            var customerRegistrationService = EngineContext.Current.Resolve<ICustomerRegistrationService>();
            await customerRegistrationService.ChangePasswordAsync(new ChangePasswordRequest(defaultUserEmail, false,
                 PasswordFormat.Hashed, defaultUserPassword, null, NopCustomerServicesDefaults.DefaultHashedPasswordFormat));

            //search engine (crawler) built-in user
            var searchEngineUser = new Customer
            {
                Email = "builtin@search_engine_record.com",
                CustomerGuid = Guid.NewGuid(),
                AdminComment = "Built-in system guest record used for requests from search engines.",
                Active = true,
                IsSystemAccount = true,
                SystemName = NopCustomerDefaults.SearchEngineCustomerName,
                CreatedOnUtc = DateTime.UtcNow,
                LastActivityDateUtc = DateTime.UtcNow,
                RegisteredInStoreId = storeId
            };

            await InsertInstallationDataAsync(searchEngineUser);

            await InsertInstallationDataAsync(new CustomerCustomerRoleMapping { CustomerRoleId = crGuests.Id, CustomerId = searchEngineUser.Id });

            //built-in user for background tasks
            var backgroundTaskUser = new Customer
            {
                Email = "builtin@background-task-record.com",
                CustomerGuid = Guid.NewGuid(),
                AdminComment = "Built-in system record used for background tasks.",
                Active = true,
                IsSystemAccount = true,
                SystemName = NopCustomerDefaults.BackgroundTaskCustomerName,
                CreatedOnUtc = DateTime.UtcNow,
                LastActivityDateUtc = DateTime.UtcNow,
                RegisteredInStoreId = storeId
            };

            await InsertInstallationDataAsync(backgroundTaskUser);

            await InsertInstallationDataAsync(new CustomerCustomerRoleMapping { CustomerId = backgroundTaskUser.Id, CustomerRoleId = crGuests.Id });
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallEmailAccountsAsync()
        {
            var emailAccounts = new List<EmailAccount>
            {
                new EmailAccount
                {
                    Email = "test@mail.com",
                    DisplayName = "Store name",
                    Host = "smtp.mail.com",
                    Port = 25,
                    Username = "123",
                    Password = "123",
                    EnableSsl = false,
                    UseDefaultCredentials = false
                }
            };

            await InsertInstallationDataAsync(emailAccounts);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallMessageTemplatesAsync()
        {
            var eaGeneral = _emailAccountRepository.Table.FirstOrDefault();
            if (eaGeneral == null)
                throw new Exception("Default email account cannot be loaded");

            var messageTemplates = new List<MessageTemplate>
            {
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.BlogCommentStoreOwnerNotification,
                    Subject = "%Store.Name%. New blog comment.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}A new blog comment has been created for blog post \"%BlogComment.BlogPostTitle%\".{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.BackInStockNotification,
                    Subject = "%Store.Name%. Back in stock notification",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%,{Environment.NewLine}<br />{Environment.NewLine}Product <a target=\"_blank\" href=\"%BackInStockSubscription.ProductUrl%\">%BackInStockSubscription.ProductName%</a> is in stock.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.CustomerEmailValidationMessage,
                    Subject = "%Store.Name%. Email validation",
                    Body = $"<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}To activate your account <a href=\"%Customer.AccountActivationURL%\">click here</a>.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Store.Name%{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.CustomerEmailRevalidationMessage,
                    Subject = "%Store.Name%. Email validation",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%!{Environment.NewLine}<br />{Environment.NewLine}To validate your new email address <a href=\"%Customer.EmailRevalidationURL%\">click here</a>.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Store.Name%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.PrivateMessageNotification,
                    Subject = "%Store.Name%. You have received a new private message",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}You have received a new private message.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.CustomerPasswordRecoveryMessage,
                    Subject = "%Store.Name%. Password recovery",
                    Body = $"<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}To change your password <a href=\"%Customer.PasswordRecoveryURL%\">click here</a>.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Store.Name%{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.CustomerWelcomeMessage,
                    Subject = "Welcome to %Store.Name%",
                    Body = $"We welcome you to <a href=\"%Store.URL%\"> %Store.Name%</a>.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}You can now take part in the various services we have to offer you. Some of these services include:{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Permanent Cart - Any products added to your online cart remain there until you remove them, or check them out.{Environment.NewLine}<br />{Environment.NewLine}Address Book - We can now deliver your products to another address other than yours! This is perfect to send birthday gifts direct to the birthday-person themselves.{Environment.NewLine}<br />{Environment.NewLine}Order History - View your history of purchases that you have made with us.{Environment.NewLine}<br />{Environment.NewLine}Products Reviews - Share your opinions on products with our other customers.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}For help with any of our online services, please email the store-owner: <a href=\"mailto:%Store.Email%\">%Store.Email%</a>.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Note: This email address was provided on our registration page. If you own the email and did not register on our site, please send an email to <a href=\"mailto:%Store.Email%\">%Store.Email%</a>.{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewForumPostMessage,
                    Subject = "%Store.Name%. New Post Notification.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}A new post has been created in the topic <a href=\"%Forums.TopicURL%\">\"%Forums.TopicName%\"</a> at <a href=\"%Forums.ForumURL%\">\"%Forums.ForumName%\"</a> forum.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Click <a href=\"%Forums.TopicURL%\">here</a> for more info.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Post author: %Forums.PostAuthor%{Environment.NewLine}<br />{Environment.NewLine}Post body: %Forums.PostBody%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewForumTopicMessage,
                    Subject = "%Store.Name%. New Topic Notification.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}A new topic <a href=\"%Forums.TopicURL%\">\"%Forums.TopicName%\"</a> has been created at <a href=\"%Forums.ForumURL%\">\"%Forums.ForumName%\"</a> forum.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Click <a href=\"%Forums.TopicURL%\">here</a> for more info.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.GiftCardNotification,
                    Subject = "%GiftCard.SenderName% has sent you a gift card for %Store.Name%",
                    Body = $"<p>{Environment.NewLine}You have received a gift card for %Store.Name%{Environment.NewLine}</p>{Environment.NewLine}<p>{Environment.NewLine}Dear %GiftCard.RecipientName%,{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%GiftCard.SenderName% (%GiftCard.SenderEmail%) has sent you a %GiftCard.Amount% gift card for <a href=\"%Store.URL%\"> %Store.Name%</a>{Environment.NewLine}</p>{Environment.NewLine}<p>{Environment.NewLine}Your gift card code is %GiftCard.CouponCode%{Environment.NewLine}</p>{Environment.NewLine}<p>{Environment.NewLine}%GiftCard.Message%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.CustomerRegisteredStoreOwnerNotification,
                    Subject = "%Store.Name%. New customer registration",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}A new customer registered with your store. Below are the customer's details:{Environment.NewLine}<br />{Environment.NewLine}Full name: %Customer.FullName%{Environment.NewLine}<br />{Environment.NewLine}Email: %Customer.Email%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewReturnRequestStoreOwnerNotification,
                    Subject = "%Store.Name%. New return request.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Customer.FullName% has just submitted a new return request. Details are below:{Environment.NewLine}<br />{Environment.NewLine}Request ID: %ReturnRequest.CustomNumber%{Environment.NewLine}<br />{Environment.NewLine}Product: %ReturnRequest.Product.Quantity% x Product: %ReturnRequest.Product.Name%{Environment.NewLine}<br />{Environment.NewLine}Reason for return: %ReturnRequest.Reason%{Environment.NewLine}<br />{Environment.NewLine}Requested action: %ReturnRequest.RequestedAction%{Environment.NewLine}<br />{Environment.NewLine}Customer comments:{Environment.NewLine}<br />{Environment.NewLine}%ReturnRequest.CustomerComment%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewReturnRequestCustomerNotification,
                    Subject = "%Store.Name%. New return request.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%!{Environment.NewLine}<br />{Environment.NewLine}You have just submitted a new return request. Details are below:{Environment.NewLine}<br />{Environment.NewLine}Request ID: %ReturnRequest.CustomNumber%{Environment.NewLine}<br />{Environment.NewLine}Product: %ReturnRequest.Product.Quantity% x Product: %ReturnRequest.Product.Name%{Environment.NewLine}<br />{Environment.NewLine}Reason for return: %ReturnRequest.Reason%{Environment.NewLine}<br />{Environment.NewLine}Requested action: %ReturnRequest.RequestedAction%{Environment.NewLine}<br />{Environment.NewLine}Customer comments:{Environment.NewLine}<br />{Environment.NewLine}%ReturnRequest.CustomerComment%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewsCommentStoreOwnerNotification,
                    Subject = "%Store.Name%. New news comment.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}A new news comment has been created for news \"%NewsComment.NewsTitle%\".{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewsletterSubscriptionActivationMessage,
                    Subject = "%Store.Name%. Subscription activation message.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%NewsLetterSubscription.ActivationUrl%\">Click here to confirm your subscription to our list.</a>{Environment.NewLine}</p>{Environment.NewLine}<p>{Environment.NewLine}If you received this email by mistake, simply delete it.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewsletterSubscriptionDeactivationMessage,
                    Subject = "%Store.Name%. Subscription deactivation message.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%NewsLetterSubscription.DeactivationUrl%\">Click here to unsubscribe from our newsletter.</a>{Environment.NewLine}</p>{Environment.NewLine}<p>{Environment.NewLine}If you received this email by mistake, simply delete it.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewVatSubmittedStoreOwnerNotification,
                    Subject = "%Store.Name%. New VAT number is submitted.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Customer.FullName% (%Customer.Email%) has just submitted a new VAT number. Details are below:{Environment.NewLine}<br />{Environment.NewLine}VAT number: %Customer.VatNumber%{Environment.NewLine}<br />{Environment.NewLine}VAT number status: %Customer.VatNumberStatus%{Environment.NewLine}<br />{Environment.NewLine}Received name: %VatValidationResult.Name%{Environment.NewLine}<br />{Environment.NewLine}Received address: %VatValidationResult.Address%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderCancelledCustomerNotification,
                    Subject = "%Store.Name%. Your order cancelled",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%,{Environment.NewLine}<br />{Environment.NewLine}Your order has been cancelled. Below is the summary of the order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a target=\"_blank\" href=\"%Order.OrderURLForCustomer%\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% %Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderCompletedCustomerNotification,
                    Subject = "%Store.Name%. Your order completed",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%,{Environment.NewLine}<br />{Environment.NewLine}Your order has been completed. Below is the summary of the order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a target=\"_blank\" href=\"%Order.OrderURLForCustomer%\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% %Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ShipmentDeliveredCustomerNotification,
                    Subject = "Your order from %Store.Name% has been %if (!%Order.IsCompletelyDelivered%) partially endif%delivered.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\"> %Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%,{Environment.NewLine}<br />{Environment.NewLine}Good news! You order has been %if (!%Order.IsCompletelyDelivered%) partially endif%delivered.{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a href=\"%Order.OrderURLForCustomer%\" target=\"_blank\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% Delivered Products:{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Shipment.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPlacedCustomerNotification,
                    Subject = "Order receipt from %Store.Name%.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%,{Environment.NewLine}<br />{Environment.NewLine}Thanks for buying from <a href=\"%Store.URL%\">%Store.Name%</a>. Below is the summary of the order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a target=\"_blank\" href=\"%Order.OrderURLForCustomer%\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% %Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPlacedStoreOwnerNotification,
                    Subject = "%Store.Name%. Purchase Receipt for Order #%Order.OrderNumber%",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Order.CustomerFullName% (%Order.CustomerEmail%) has just placed an order from your store. Below is the summary of the order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% %Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ShipmentSentCustomerNotification,
                    Subject = "Your order from %Store.Name% has been %if (!%Order.IsCompletelyShipped%) partially endif%shipped.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\"> %Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%!,{Environment.NewLine}<br />{Environment.NewLine}Good news! You order has been %if (!%Order.IsCompletelyShipped%) partially endif%shipped.{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a href=\"%Order.OrderURLForCustomer%\" target=\"_blank\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% Shipped Products:{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Shipment.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ShipmentReadyForPickupCustomerNotification,
                    Subject = "Your order from %Store.Name% has been %if (!%Order.IsCompletelyReadyForPickup%) partially endif%ready for pickup.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\"> %Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%!,{Environment.NewLine}<br />{Environment.NewLine}Good news! You order has been %if (!%Order.IsCompletelyReadyForPickup%) partially endif%ready for pickup.{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a href=\"%Order.OrderURLForCustomer%\" target=\"_blank\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% Products ready for pickup:{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Shipment.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ProductReviewStoreOwnerNotification,
                    Subject = "%Store.Name%. New product review.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}A new product review has been written for product \"%ProductReview.ProductName%\".{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ProductReviewReplyCustomerNotification,
                    Subject = "%Store.Name%. Product review reply.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%,{Environment.NewLine}<br />{Environment.NewLine}You received a reply from the store administration to your review for product \"%ProductReview.ProductName%\".{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.QuantityBelowStoreOwnerNotification,
                    Subject = "%Store.Name%. Quantity below notification. %Product.Name%",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Product.Name% (ID: %Product.ID%) low quantity.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Quantity: %Product.StockQuantity%{Environment.NewLine}<br />{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.QuantityBelowAttributeCombinationStoreOwnerNotification,
                    Subject = "%Store.Name%. Quantity below notification. %Product.Name%",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Product.Name% (ID: %Product.ID%) low quantity.{Environment.NewLine}<br />{Environment.NewLine}%AttributeCombination.Formatted%{Environment.NewLine}<br />{Environment.NewLine}Quantity: %AttributeCombination.StockQuantity%{Environment.NewLine}<br />{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ReturnRequestStatusChangedCustomerNotification,
                    Subject = "%Store.Name%. Return request status was changed.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%,{Environment.NewLine}<br />{Environment.NewLine}Your return request #%ReturnRequest.CustomNumber% status has been changed.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.EmailAFriendMessage,
                    Subject = "%Store.Name%. Referred Item",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\"> %Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%EmailAFriend.Email% was shopping on %Store.Name% and wanted to share the following item with you.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<b><a target=\"_blank\" href=\"%Product.ProductURLForCustomer%\">%Product.Name%</a></b>{Environment.NewLine}<br />{Environment.NewLine}%Product.ShortDescription%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}For more info click <a target=\"_blank\" href=\"%Product.ProductURLForCustomer%\">here</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%EmailAFriend.PersonalMessage%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Store.Name%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.WishlistToFriendMessage,
                    Subject = "%Store.Name%. Wishlist",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\"> %Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Wishlist.Email% was shopping on %Store.Name% and wanted to share a wishlist with you.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}For more info click <a target=\"_blank\" href=\"%Wishlist.URLForCustomer%\">here</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Wishlist.PersonalMessage%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Store.Name%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewOrderNoteAddedCustomerNotification,
                    Subject = "%Store.Name%. New order note has been added",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%,{Environment.NewLine}<br />{Environment.NewLine}New order note has been added to your account:{Environment.NewLine}<br />{Environment.NewLine}\"%Order.NewNoteText%\".{Environment.NewLine}<br />{Environment.NewLine}<a target=\"_blank\" href=\"%Order.OrderURLForCustomer%\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.RecurringPaymentCancelledStoreOwnerNotification,
                    Subject = "%Store.Name%. Recurring payment cancelled",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%RecurringPayment.CancelAfterFailedPayment%) The last payment for the recurring payment ID=%RecurringPayment.ID% failed, so it was cancelled. endif% %if (!%RecurringPayment.CancelAfterFailedPayment%) %Customer.FullName% (%Customer.Email%) has just cancelled a recurring payment ID=%RecurringPayment.ID%. endif%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.RecurringPaymentCancelledCustomerNotification,
                    Subject = "%Store.Name%. Recurring payment cancelled",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%,{Environment.NewLine}<br />{Environment.NewLine}%if (%RecurringPayment.CancelAfterFailedPayment%) It appears your credit card didn't go through for this recurring payment (<a href=\"%Order.OrderURLForCustomer%\" target=\"_blank\">%Order.OrderURLForCustomer%</a>){Environment.NewLine}<br />{Environment.NewLine}So your subscription has been cancelled. endif% %if (!%RecurringPayment.CancelAfterFailedPayment%) The recurring payment ID=%RecurringPayment.ID% was cancelled. endif%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.RecurringPaymentFailedCustomerNotification,
                    Subject = "%Store.Name%. Last recurring payment failed",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Customer.FullName%,{Environment.NewLine}<br />{Environment.NewLine}It appears your credit card didn't go through for this recurring payment (<a href=\"%Order.OrderURLForCustomer%\" target=\"_blank\">%Order.OrderURLForCustomer%</a>){Environment.NewLine}<br /> %if (%RecurringPayment.RecurringPaymentType% == \"Manual\") {Environment.NewLine}You can recharge balance and manually retry payment or cancel it on the order history page. endif% %if (%RecurringPayment.RecurringPaymentType% == \"Automatic\") {Environment.NewLine}You can recharge balance and wait, we will try to make the payment again, or you can cancel it on the order history page. endif%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPlacedVendorNotification,
                    Subject = "%Store.Name%. Order placed",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Customer.FullName% (%Customer.Email%) has just placed an order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPlacedAffiliateNotification,
                    Subject = "%Store.Name%. Order placed",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Customer.FullName% (%Customer.Email%) has just placed an order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderRefundedCustomerNotification,
                    Subject = "%Store.Name%. Order #%Order.OrderNumber% refunded",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%,{Environment.NewLine}<br />{Environment.NewLine}Thanks for buying from <a href=\"%Store.URL%\">%Store.Name%</a>. Order #%Order.OrderNumber% has been has been refunded. Please allow 7-14 days for the refund to be reflected in your account.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Amount refunded: %Order.AmountRefunded%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Below is the summary of the order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a href=\"%Order.OrderURLForCustomer%\" target=\"_blank\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br /{Environment.NewLine}>Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% %Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderRefundedStoreOwnerNotification,
                    Subject = "%Store.Name%. Order #%Order.OrderNumber% refunded",
                    Body = $"%Store.Name%. Order #%Order.OrderNumber% refunded', N'{Environment.NewLine}<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order #%Order.OrderNumber% has been just refunded{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Amount refunded: %Order.AmountRefunded%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPaidStoreOwnerNotification,
                    Subject = "%Store.Name%. Order #%Order.OrderNumber% paid",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order #%Order.OrderNumber% has been just paid{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPaidCustomerNotification,
                    Subject = "%Store.Name%. Order #%Order.OrderNumber% paid",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Hello %Order.CustomerFullName%,{Environment.NewLine}<br />{Environment.NewLine}Thanks for buying from <a href=\"%Store.URL%\">%Store.Name%</a>. Order #%Order.OrderNumber% has been just paid. Below is the summary of the order.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Order Details: <a href=\"%Order.OrderURLForCustomer%\" target=\"_blank\">%Order.OrderURLForCustomer%</a>{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Billing Address{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingFirstName% %Order.BillingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingCity% %Order.BillingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.BillingStateProvince% %Order.BillingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%if (%Order.Shippable%) Shipping Address{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingFirstName% %Order.ShippingLastName%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress1%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingAddress2%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingCity% %Order.ShippingZipPostalCode%{Environment.NewLine}<br />{Environment.NewLine}%Order.ShippingStateProvince% %Order.ShippingCountry%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Shipping Method: %Order.ShippingMethod%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine} endif% %Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPaidVendorNotification,
                    Subject = "%Store.Name%. Order #%Order.OrderNumber% paid",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order #%Order.OrderNumber% has been just paid.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.OrderPaidAffiliateNotification,
                    Subject = "%Store.Name%. Order #%Order.OrderNumber% paid",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order #%Order.OrderNumber% has been just paid.{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Order Number: %Order.OrderNumber%{Environment.NewLine}<br />{Environment.NewLine}Date Ordered: %Order.CreatedOn%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Order.Product(s)%{Environment.NewLine}</p>{Environment.NewLine}",
                    //this template is disabled by default
                    IsActive = false,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.NewVendorAccountApplyStoreOwnerNotification,
                    Subject = "%Store.Name%. New vendor account submitted.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}%Customer.FullName% (%Customer.Email%) has just submitted for a vendor account. Details are below:{Environment.NewLine}<br />{Environment.NewLine}Vendor name: %Vendor.Name%{Environment.NewLine}<br />{Environment.NewLine}Vendor email: %Vendor.Email%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}You can activate it in admin area.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.VendorInformationChangeStoreOwnerNotification,
                    Subject = "%Store.Name%. Vendor information change.",
                    Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}Vendor %Vendor.Name% (%Vendor.Email%) has just changed information about itself.{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ContactUsMessage,
                    Subject = "%Store.Name%. Contact us",
                    Body = $"<p>{Environment.NewLine}%ContactUs.Body%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                },
                new MessageTemplate
                {
                    Name = MessageTemplateSystemNames.ContactVendorMessage,
                    Subject = "%Store.Name%. Contact us",
                    Body = $"<p>{Environment.NewLine}%ContactUs.Body%{Environment.NewLine}</p>{Environment.NewLine}",
                    IsActive = true,
                    EmailAccountId = eaGeneral.Id
                }
            };

            await InsertInstallationDataAsync(messageTemplates);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallTopicsAsync()
        {
            var defaultTopicTemplate =
                _topicTemplateRepository.Table.FirstOrDefault(tt => tt.Name == "Default template");
            if (defaultTopicTemplate == null)
                throw new Exception("Topic template cannot be loaded");

            var topics = new List<Topic>
            {
                new Topic
                {
                    SystemName = "AboutUs",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    IncludeInFooterColumn1 = true,
                    DisplayOrder = 20,
                    Published = true,
                    Title = "About us",
                    Body =
                        "<p>Put your &quot;About Us&quot; information here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "CheckoutAsGuestOrRegister",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = string.Empty,
                    Body =
                        "<p><strong>Register and save time!</strong><br />Register with us for future convenience:</p><ul><li>Fast and easy check out</li><li>Easy access to your order history and status</li></ul>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "ConditionsOfUse",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    IncludeInFooterColumn1 = true,
                    DisplayOrder = 15,
                    Published = true,
                    Title = "Conditions of Use",
                    Body = "<p>Put your conditions of use information here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "ContactUs",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = string.Empty,
                    Body = "<p>Put your contact information here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "ForumWelcomeMessage",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = "Forums",
                    Body = "<p>Put your welcome message here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "HomepageText",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = "Welcome to our store",
                    Body =
                        "<p>Online shopping is the process consumers go through to purchase products or services over the Internet. You can edit this in the admin site.</p><p>If you have questions, see the <a href=\"http://docs.nopcommerce.com/\">Documentation</a>, or post in the <a href=\"https://www.nopcommerce.com/boards/\">Forums</a> at <a href=\"https://www.nopcommerce.com\">nopCommerce.com</a></p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "LoginRegistrationInfo",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = "About login / registration",
                    Body =
                        "<p>Put your login / registration information here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "PrivacyInfo",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    IncludeInFooterColumn1 = true,
                    DisplayOrder = 10,
                    Published = true,
                    Title = "Privacy notice",
                    Body = "<p>Put your privacy policy information here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "PageNotFound",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = string.Empty,
                    Body =
                        "<p><strong>The page you requested was not found, and we have a fine guess why.</strong></p><ul><li>If you typed the URL directly, please make sure the spelling is correct.</li><li>The page no longer exists. In this case, we profusely apologize for the inconvenience and for any damage this may cause.</li></ul>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "ShippingInfo",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    IncludeInFooterColumn1 = true,
                    DisplayOrder = 5,
                    Published = true,
                    Title = "Shipping & returns",
                    Body =
                        "<p>Put your shipping &amp; returns information here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "ApplyVendor",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = string.Empty,
                    Body = "<p>Put your apply vendor instructions here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                },
                new Topic
                {
                    SystemName = "VendorTermsOfService",
                    IncludeInSitemap = false,
                    IsPasswordProtected = false,
                    IncludeInFooterColumn1 = false,
                    DisplayOrder = 1,
                    Published = true,
                    Title = "Terms of services for vendors",
                    Body = "<p>Put your terms of service information here. You can edit this in the admin site.</p>",
                    TopicTemplateId = defaultTopicTemplate.Id
                }
            };

            await InsertInstallationDataAsync(topics);

            //search engine names
            foreach (var topic in topics)
            {
                await InsertInstallationDataAsync(new UrlRecord
                {
                    EntityId = topic.Id,
                    EntityName = nameof(Topic),
                    LanguageId = 0,
                    IsActive = true,
                    Slug = await ValidateSeNameAsync(topic, !string.IsNullOrEmpty(topic.Title) ? topic.Title : topic.SystemName)
                });
            }
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallSettingsAsync(RegionInfo regionInfo)
        {
            var isMetric = regionInfo?.IsMetric ?? false;
            var country = regionInfo?.TwoLetterISORegionName ?? string.Empty;
            var isGermany = country == "DE";
            var isEurope = ISO3166.FromCountryCode(country)?.SubjectToVat ?? false;

            var settingService = EngineContext.Current.Resolve<ISettingService>();
            await settingService.SaveSettingAsync(new PdfSettings
            {
                LogoPictureId = 0,
                LetterPageSizeEnabled = false,
                RenderOrderNotes = true,
                FontFamily = "FreeSerif",
                InvoiceFooterTextColumn1 = null,
                InvoiceFooterTextColumn2 = null
            });

            await settingService.SaveSettingAsync(new SitemapSettings
            {
                SitemapEnabled = true,
                SitemapPageSize = 200,
                SitemapIncludeCategories = true,
                SitemapIncludeManufacturers = true,
                SitemapIncludeProducts = false,
                SitemapIncludeProductTags = false,
                SitemapIncludeBlogPosts = true,
                SitemapIncludeNews = false,
                SitemapIncludeTopics = true
            });

            await settingService.SaveSettingAsync(new SitemapXmlSettings
            {
                SitemapXmlEnabled = true,
                SitemapXmlIncludeBlogPosts = true,
                SitemapXmlIncludeCategories = true,
                SitemapXmlIncludeManufacturers = true,
                SitemapXmlIncludeNews = true,
                SitemapXmlIncludeProducts = true,
                SitemapXmlIncludeProductTags = true,
                SitemapXmlIncludeCustomUrls = true,
                SitemapXmlIncludeTopics = true,
                RebuildSitemapXmlAfterHours = 2 * 24,
                SitemapBuildOperationDelay = 60
            });

            await settingService.SaveSettingAsync(new CommonSettings
            {
                UseSystemEmailForContactUsForm = true,
                DisplayJavaScriptDisabledWarning = false,
                Log404Errors = true,
                BreadcrumbDelimiter = "/",
                BbcodeEditorOpenLinksInNewWindow = false,
                PopupForTermsOfServiceLinks = true,
                JqueryMigrateScriptLoggingActive = false,
                UseResponseCompression = true,
                FaviconAndAppIconsHeadCode = "<link rel=\"apple-touch-icon\" sizes=\"180x180\" href=\"/icons/icons_0/apple-touch-icon.png\"><link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"/icons/icons_0/favicon-32x32.png\"><link rel=\"icon\" type=\"image/png\" sizes=\"192x192\" href=\"/icons/icons_0/android-chrome-192x192.png\"><link rel=\"icon\" type=\"image/png\" sizes=\"16x16\" href=\"/icons/icons_0/favicon-16x16.png\"><link rel=\"manifest\" href=\"/icons/icons_0/site.webmanifest\"><link rel=\"mask-icon\" href=\"/icons/icons_0/safari-pinned-tab.svg\" color=\"#5bbad5\"><link rel=\"shortcut icon\" href=\"/icons/icons_0/favicon.ico\"><meta name=\"msapplication-TileColor\" content=\"#2d89ef\"><meta name=\"msapplication-TileImage\" content=\"/icons/icons_0/mstile-144x144.png\"><meta name=\"msapplication-config\" content=\"/icons/icons_0/browserconfig.xml\"><meta name=\"theme-color\" content=\"#ffffff\">",
                EnableHtmlMinification = true,
                RestartTimeout = NopCommonDefaults.RestartTimeout
            });

            // TODO: Tuesday, February 21, 2023, Add Store Name And Description as HomePage title
            await settingService.SaveSettingAsync(new SeoSettings
            {
                PageTitleSeparator = ". ",
                PageTitleSeoAdjustment = PageTitleSeoAdjustment.PagenameAfterStorename,
                GenerateProductMetaDescription = true,
                ConvertNonWesternChars = false,
                AllowUnicodeCharsInUrls = true,
                CanonicalUrlsEnabled = false,
                QueryStringInCanonicalUrlsEnabled = false,
                WwwRequirement = WwwRequirement.NoMatter,
                TwitterMetaTags = true,
                OpenGraphMetaTags = true,
                MicrodataEnabled = true,
                ReservedUrlRecordSlugs = NopSeoDefaults.ReservedUrlRecordSlugs,
                CustomHeadTags = string.Empty
            });

            await settingService.SaveSettingAsync(new AdminAreaSettings
            {
                DefaultGridPageSize = 15,
                PopupGridPageSize = 7,
                GridPageSizes = "7, 15, 20, 50, 100",
                RichEditorAdditionalSettings = null,
                RichEditorAllowJavaScript = false,
                RichEditorAllowStyleTag = false,
                UseRichEditorForCustomerEmails = false,
                UseRichEditorInMessageTemplates = false,
                CheckLicense = true,
                UseIsoDateFormatInJsonResult = true,
                ShowDocumentationReferenceLinks = true
            });

            await settingService.SaveSettingAsync(new ProductEditorSettings
            {
                Weight = true,
                Dimensions = true,
                ProductAttributes = true,
                SpecificationAttributes = true,
                PAngV = isGermany
            });

            await settingService.SaveSettingAsync(new GdprSettings
            {
                GdprEnabled = false,
                LogPrivacyPolicyConsent = true,
                LogNewsletterConsent = true,
                LogUserProfileChanges = true
            });

            await settingService.SaveSettingAsync(new CatalogSettings
            {
                AllowViewUnpublishedProductPage = true,
                DisplayDiscontinuedMessageForUnpublishedProducts = true,
                PublishBackProductWhenCancellingOrders = false,
                ShowSkuOnProductDetailsPage = true,
                ShowSkuOnCatalogPages = false,
                ShowManufacturerPartNumber = false,
                ShowGtin = false,
                ShowFreeShippingNotification = true,
                ShowShortDescriptionOnCatalogPages = false,
                AllowProductSorting = true,
                AllowProductViewModeChanging = true,
                DefaultViewMode = "grid",
                ShowProductsFromSubcategories = false,
                ShowCategoryProductNumber = true,
                ShowCategoryProductNumberIncludingSubcategories = true,
                CategoryBreadcrumbEnabled = true,
                ShowShareButton = false,
                PageShareCode = "<!-- AddThis Button BEGIN --><div class=\"addthis_toolbox addthis_default_style \"><a class=\"addthis_button_preferred_1\"></a><a class=\"addthis_button_preferred_2\"></a><a class=\"addthis_button_preferred_3\"></a><a class=\"addthis_button_preferred_4\"></a><a class=\"addthis_button_compact\"></a><a class=\"addthis_counter addthis_bubble_style\"></a></div><script src=\"http://s7.addthis.com/js/250/addthis_widget.js#pubid=nopsolutions\"></script><!-- AddThis Button END -->",
                ProductReviewsMustBeApproved = true,
                OneReviewPerProductFromCustomer = false,
                DefaultProductRatingValue = 5,
                AllowAnonymousUsersToReviewProduct = false,
                ProductReviewPossibleOnlyAfterPurchasing = true,
                NotifyStoreOwnerAboutNewProductReviews = false,
                NotifyCustomerAboutProductReviewReply = false,
                EmailAFriendEnabled = false,
                AllowAnonymousUsersToEmailAFriend = false,
                RecentlyViewedProductsNumber = 3,
                RecentlyViewedProductsEnabled = true,
                NewProductsEnabled = true,
                NewProductsPageSize = 6,
                NewProductsAllowCustomersToSelectPageSize = true,
                NewProductsPageSizeOptions = "6, 3, 9",
                CompareProductsEnabled = true,
                CompareProductsNumber = 4,
                ProductSearchAutoCompleteEnabled = true,
                ProductSearchEnabled = true,
                ProductSearchAutoCompleteNumberOfProducts = 10,
                ShowLinkToAllResultInSearchAutoComplete = false,
                ProductSearchTermMinimumLength = 3,
                ShowProductImagesInSearchAutoComplete = false,
                ShowBestsellersOnHomepage = false,
                NumberOfBestsellersOnHomepage = 4,
                SearchPageProductsPerPage = 6,
                SearchPageAllowCustomersToSelectPageSize = true,
                SearchPagePageSizeOptions = "6, 3, 9, 18",
                SearchPagePriceRangeFiltering = true,
                SearchPageManuallyPriceRange = false,
                SearchPagePriceFrom = NopCatalogDefaults.DefaultPriceRangeFrom,
                SearchPagePriceTo = NopCatalogDefaults.DefaultPriceRangeTo,
                ProductsAlsoPurchasedEnabled = true,
                ProductsAlsoPurchasedNumber = 4,
                AjaxProcessAttributeChange = true,
                NumberOfProductTags = 15,
                ProductsByTagPageSize = 6,
                IncludeShortDescriptionInCompareProducts = false,
                IncludeFullDescriptionInCompareProducts = false,
                IncludeFeaturedProductsInNormalLists = false,
                UseLinksInRequiredProductWarnings = true,
                DisplayTierPricesWithDiscounts = true,
                IgnoreDiscounts = false,
                IgnoreFeaturedProducts = false,
                IgnoreAcl = true,
                IgnoreStoreLimitations = true,
                CacheProductPrices = false,
                ProductsByTagAllowCustomersToSelectPageSize = true,
                ProductsByTagPageSizeOptions = "6, 3, 9, 18",
                ProductsByTagPriceRangeFiltering = true,
                ProductsByTagManuallyPriceRange = false,
                ProductsByTagPriceFrom = NopCatalogDefaults.DefaultPriceRangeFrom,
                ProductsByTagPriceTo = NopCatalogDefaults.DefaultPriceRangeTo,
                MaximumBackInStockSubscriptions = 200,
                ManufacturersBlockItemsToDisplay = 2,
                DisplayTaxShippingInfoFooter = isGermany,
                DisplayTaxShippingInfoProductDetailsPage = isGermany,
                DisplayTaxShippingInfoProductBoxes = isGermany,
                DisplayTaxShippingInfoShoppingCart = isGermany,
                DisplayTaxShippingInfoWishlist = isGermany,
                DisplayTaxShippingInfoOrderDetailsPage = isGermany,
                DefaultCategoryPageSizeOptions = "6, 3, 9",
                DefaultCategoryPageSize = 6,
                DefaultManufacturerPageSizeOptions = "6, 3, 9",
                DefaultManufacturerPageSize = 6,
                ShowProductReviewsTabOnAccountPage = true,
                ProductReviewsPageSizeOnAccountPage = 10,
                ProductReviewsSortByCreatedDateAscending = false,
                ExportImportProductAttributes = true,
                ExportImportProductSpecificationAttributes = true,
                ExportImportUseDropdownlistsForAssociatedEntities = true,
                ExportImportProductsCountInOneFile = 500,
                ExportImportSplitProductsFile = false,
                ExportImportRelatedEntitiesByName = true,
                CountDisplayedYearsDatePicker = 1,
                UseAjaxLoadMenu = false,
                UseAjaxCatalogProductsLoading = true,
                EnableManufacturerFiltering = true,
                EnablePriceRangeFiltering = true,
                EnableSpecificationAttributeFiltering = true,
                AttributeValueOutOfStockDisplayType = AttributeValueOutOfStockDisplayType.AlwaysDisplay
            });

            await settingService.SaveSettingAsync(new LocalizationSettings
            {
                DefaultAdminLanguageId = _languageRepository.Table.Single(l => l.LanguageCulture == NopCommonDefaults.DefaultLanguageCulture).Id,
                UseImagesForLanguageSelection = false,
                SeoFriendlyUrlsForLanguagesEnabled = false,
                AutomaticallyDetectLanguage = false,
                LoadAllLocaleRecordsOnStartup = true,
                LoadAllLocalizedPropertiesOnStartup = true,
                LoadAllUrlRecordsOnStartup = false,
                IgnoreRtlPropertyForAdminArea = false
            });

            await settingService.SaveSettingAsync(new CustomerSettings
            {
                UsernamesEnabled = false,
                CheckUsernameAvailabilityEnabled = false,
                AllowUsersToChangeUsernames = false,
                DefaultPasswordFormat = PasswordFormat.Hashed,
                HashedPasswordFormat = NopCustomerServicesDefaults.DefaultHashedPasswordFormat,
                PasswordMinLength = 6,
                PasswordRequireDigit = false,
                PasswordRequireLowercase = false,
                PasswordRequireNonAlphanumeric = false,
                PasswordRequireUppercase = false,
                UnduplicatedPasswordsNumber = 4,
                PasswordRecoveryLinkDaysValid = 7,
                PasswordLifetime = 90,
                FailedPasswordAllowedAttempts = 0,
                FailedPasswordLockoutMinutes = 30,
                UserRegistrationType = UserRegistrationType.Standard,
                AllowCustomersToUploadAvatars = false,
                AvatarMaximumSizeBytes = 20000,
                DefaultAvatarEnabled = true,
                ShowCustomersLocation = false,
                ShowCustomersJoinDate = false,
                AllowViewingProfiles = false,
                NotifyNewCustomerRegistration = false,
                HideDownloadableProductsTab = true,
                HideBackInStockSubscriptionsTab = true,
                DownloadableProductsValidateUser = false,
                CustomerNameFormat = CustomerNameFormat.ShowFirstName,
                FirstNameEnabled = true,
                FirstNameRequired = true,
                LastNameEnabled = true,
                LastNameRequired = true,
                GenderEnabled = true,
                DateOfBirthEnabled = true,
                DateOfBirthRequired = false,
                DateOfBirthMinimumAge = null,
                CompanyEnabled = true,
                StreetAddressEnabled = false,
                StreetAddress2Enabled = false,
                ZipPostalCodeEnabled = false,
                CityEnabled = false,
                CountyEnabled = false,
                CountyRequired = false,
                CountryEnabled = false,
                CountryRequired = false,
                StateProvinceEnabled = false,
                StateProvinceRequired = false,
                PhoneEnabled = false,
                FaxEnabled = false,
                AcceptPrivacyPolicyEnabled = false,
                NewsletterEnabled = false,
                NewsletterTickedByDefault = true,
                HideNewsletterBlock = true,
                NewsletterBlockAllowToUnsubscribe = false,
                OnlineCustomerMinutes = 20,
                StoreLastVisitedPage = false,
                StoreIpAddresses = true,
                LastActivityMinutes = 15,
                SuffixDeletedCustomers = false,
                EnteringEmailTwice = false,
                RequireRegistrationForDownloadableProducts = false,
                AllowCustomersToCheckGiftCardBalance = false,
                DeleteGuestTaskOlderThanMinutes = 1440,
                PhoneNumberValidationEnabled = false,
                PhoneNumberValidationUseRegex = false,
                PhoneNumberValidationRule = "^[0-9]{1,14}?$"
            });

            await settingService.SaveSettingAsync(new MultiFactorAuthenticationSettings
            {
                ForceMultifactorAuthentication = false
            });

            await settingService.SaveSettingAsync(new AddressSettings
            {
                CompanyEnabled = true,
                StreetAddressEnabled = true,
                StreetAddressRequired = true,
                StreetAddress2Enabled = true,
                ZipPostalCodeEnabled = true,
                ZipPostalCodeRequired = true,
                CityEnabled = true,
                CityRequired = true,
                CountyEnabled = false,
                CountyRequired = false,
                CountryEnabled = true,
                StateProvinceEnabled = true,
                PhoneEnabled = true,
                PhoneRequired = true,
                FaxEnabled = true
            });

            await settingService.SaveSettingAsync(new MediaSettings
            {
                AvatarPictureSize = 120,
                ProductThumbPictureSize = 415,
                ProductDetailsPictureSize = 550,
                ProductThumbPictureSizeOnProductDetailsPage = 100,
                AssociatedProductPictureSize = 220,
                CategoryThumbPictureSize = 450,
                ManufacturerThumbPictureSize = 420,
                VendorThumbPictureSize = 450,
                CartThumbPictureSize = 80,
                MiniCartThumbPictureSize = 70,
                AutoCompleteSearchThumbPictureSize = 20,
                ImageSquarePictureSize = 32,
                MaximumImageSize = 1980,
                DefaultPictureZoomEnabled = false,
                DefaultImageQuality = 80,
                MultipleThumbDirectories = false,
                ImportProductImagesUsingHash = true,
                AzureCacheControlHeader = string.Empty,
                UseAbsoluteImagePath = true
            });

            await settingService.SaveSettingAsync(new StoreInformationSettings
            {
                StoreClosed = false,
                DefaultStoreTheme = "DefaultClean",
                AllowCustomerToSelectTheme = false,
                DisplayEuCookieLawWarning = isEurope,
                FacebookLink = "https://www.facebook.com/nopCommerce",
                TwitterLink = "https://twitter.com/nopCommerce",
                YoutubeLink = "https://www.youtube.com/user/nopCommerce",
                HidePoweredByNopCommerce = false
            });

            await settingService.SaveSettingAsync(new ExternalAuthenticationSettings
            {
                RequireEmailValidation = false,
                LogErrors = false,
                AllowCustomersToRemoveAssociations = true
            });

            await settingService.SaveSettingAsync(new RewardPointsSettings
            {
                Enabled = true,
                ExchangeRate = 1,
                PointsForRegistration = 0,
                RegistrationPointsValidity = 30,
                PointsForPurchases_Amount = 10,
                PointsForPurchases_Points = 1,
                MinOrderTotalToAwardPoints = 0,
                MaximumRewardPointsToUsePerOrder = 0,
                MaximumRedeemedRate = 0,
                PurchasesPointsValidity = 45,
                ActivationDelay = 0,
                ActivationDelayPeriodId = 0,
                DisplayHowMuchWillBeEarned = true,
                PointsAccumulatedForAllStores = true,
                PageSize = 10
            });

            var primaryCurrency = "IRR";
            await settingService.SaveSettingAsync(new CurrencySettings
            {
                DisplayCurrencyLabel = false,
                PrimaryStoreCurrencyId = _currencyRepository.Table.Single(c => c.CurrencyCode == primaryCurrency).Id,
                PrimaryExchangeRateCurrencyId = _currencyRepository.Table.Single(c => c.CurrencyCode == primaryCurrency).Id,
                ActiveExchangeRateProviderSystemName = "CurrencyExchange.ECB",
                AutoUpdateEnabled = false
            });

            var baseDimension = isMetric ? "meters" : "inches";
            var baseWeight = isMetric ? "kg" : "lb";

            await settingService.SaveSettingAsync(new MeasureSettings
            {
                BaseDimensionId = _measureDimensionRepository.Table.Single(m => m.SystemKeyword == baseDimension).Id,
                BaseWeightId = _measureWeightRepository.Table.Single(m => m.SystemKeyword == baseWeight).Id
            });

            await settingService.SaveSettingAsync(new MessageTemplatesSettings
            {
                CaseInvariantReplacement = false,
                Color1 = "#b9babe",
                Color2 = "#ebecee",
                Color3 = "#dde2e6"
            });

            await settingService.SaveSettingAsync(new ShoppingCartSettings
            {
                DisplayCartAfterAddingProduct = false,
                DisplayWishlistAfterAddingProduct = false,
                MaximumShoppingCartItems = 1000,
                MaximumWishlistItems = 1000,
                AllowOutOfStockItemsToBeAddedToWishlist = false,
                MoveItemsFromWishlistToCart = true,
                CartsSharedBetweenStores = false,
                ShowProductImagesOnShoppingCart = true,
                ShowProductImagesOnWishList = true,
                ShowDiscountBox = true,
                ShowGiftCardBox = true,
                CrossSellsNumber = 4,
                EmailWishlistEnabled = true,
                AllowAnonymousUsersToEmailWishlist = false,
                MiniShoppingCartEnabled = true,
                ShowProductImagesInMiniShoppingCart = true,
                MiniShoppingCartProductNumber = 5,
                RoundPricesDuringCalculation = true,
                GroupTierPricesForDistinctShoppingCartItems = false,
                AllowCartItemEditing = true,
                RenderAssociatedAttributeValueQuantity = true
            });

            await settingService.SaveSettingAsync(new OrderSettings
            {
                ReturnRequestNumberMask = "{ID}",
                IsReOrderAllowed = true,
                MinOrderSubtotalAmount = 0,
                MinOrderSubtotalAmountIncludingTax = false,
                MinOrderTotalAmount = 0,
                AutoUpdateOrderTotalsOnEditingOrder = false,
                AnonymousCheckoutAllowed = false,
                TermsOfServiceOnShoppingCartPage = true,
                TermsOfServiceOnOrderConfirmPage = false,
                OnePageCheckoutEnabled = true,
                OnePageCheckoutDisplayOrderTotalsOnPaymentInfoTab = false,
                DisableBillingAddressCheckoutStep = false,
                DisableOrderCompletedPage = true,
                DisplayPickupInStoreOnShippingMethodPage = false,
                AttachPdfInvoiceToOrderPlacedEmail = false,
                AttachPdfInvoiceToOrderCompletedEmail = false,
                GeneratePdfInvoiceInCustomerLanguage = true,
                AttachPdfInvoiceToOrderPaidEmail = false,
                ReturnRequestsEnabled = false,
                ReturnRequestsAllowFiles = false,
                ReturnRequestsFileMaximumSize = 2048,
                NumberOfDaysReturnRequestAvailable = 365,
                MinimumOrderPlacementInterval = 30,
                ActivateGiftCardsAfterCompletingOrder = false,
                DeactivateGiftCardsAfterCancellingOrder = false,
                DeactivateGiftCardsAfterDeletingOrder = false,
                CompleteOrderWhenDelivered = true,
                CustomOrderNumberMask = "{ID}",
                ExportWithProducts = true,
                AllowAdminsToBuyCallForPriceProducts = true,
                DisplayCustomerCurrencyOnOrders = false,
                DisplayOrderSummary = true
            });

            await settingService.SaveSettingAsync(new SecuritySettings
            {
                EncryptionKey = CommonHelper.GenerateRandomDigitCode(16),
                AdminAreaAllowedIpAddresses = null,
                HoneypotEnabled = false,
                HoneypotInputName = "hpinput",
                AllowNonAsciiCharactersInHeaders = true
            });

            await settingService.SaveSettingAsync(new ShippingSettings
            {
                ActiveShippingRateComputationMethodSystemNames = new List<string> {  },
                ActivePickupPointProviderSystemNames = new List<string> { },
                ShipToSameAddress = true,
                AllowPickupInStore = true,
                DisplayPickupPointsOnMap = false,
                IgnoreAdditionalShippingChargeForPickupInStore = true,
                UseWarehouseLocation = false,
                NotifyCustomerAboutShippingFromMultipleLocations = false,
                FreeShippingOverXEnabled = false,
                FreeShippingOverXValue = decimal.Zero,
                FreeShippingOverXIncludingTax = false,
                EstimateShippingProductPageEnabled = false,
                EstimateShippingCartPageEnabled = false,
                EstimateShippingCityNameEnabled = false,
                DisplayShipmentEventsToCustomers = false,
                DisplayShipmentEventsToStoreOwner = false,
                HideShippingTotal = false,
                ReturnValidOptionsIfThereAreAny = true,
                BypassShippingMethodSelectionIfOnlyOne = false,
                UseCubeRootMethod = true,
                ConsiderAssociatedProductsDimensions = false,
                ShipSeparatelyOneItemEach = true,
                RequestDelay = 300,
                ShippingSorting = ShippingSortingEnum.ShippingCost,
            });

            await settingService.SaveSettingAsync(new PaymentSettings
            {
                ActivePaymentMethodSystemNames = new List<string> { },
                AllowRePostingPayments = true,
                BypassPaymentMethodSelectionIfOnlyOne = true,
                ShowPaymentMethodDescriptions = true,
                SkipPaymentInfoStepForRedirectionPaymentMethods = false,
                CancelRecurringPaymentsAfterFailedPayment = false,
                RegenerateOrderGuidInterval = 180
            });

            await settingService.SaveSettingAsync(new TaxSettings
            {
                TaxBasedOn = TaxBasedOn.BillingAddress,
                TaxBasedOnPickupPointAddress = false,
                TaxDisplayType = TaxDisplayType.ExcludingTax,
                // ActiveTaxProviderSystemName = "Tax.FixedOrByCountryStateZip",
                DefaultTaxAddressId = 0,
                DisplayTaxSuffix = false,
                DisplayTaxRates = false,
                PricesIncludeTax = false,
                AllowCustomersToSelectTaxDisplayType = false,
                ForceTaxExclusionFromOrderSubtotal = false,
                DefaultTaxCategoryId = 0,
                HideZeroTax = false,
                HideTaxInOrderSummary = false,
                ShippingIsTaxable = false,
                ShippingPriceIncludesTax = false,
                ShippingTaxClassId = 0,
                PaymentMethodAdditionalFeeIsTaxable = false,
                PaymentMethodAdditionalFeeIncludesTax = false,
                PaymentMethodAdditionalFeeTaxClassId = 0,
                EuVatEnabled = isEurope,
                EuVatShopCountryId = isEurope ? (_countryRepository.Table.FirstOrDefault(x => x.TwoLetterIsoCode == country)?.Id ?? 0) : 0,
                EuVatAllowVatExemption = true,
                EuVatUseWebService = false,
                EuVatAssumeValid = false,
                EuVatEmailAdminWhenNewVatSubmitted = false,
                LogErrors = false
            });

            await settingService.SaveSettingAsync(new DateTimeSettings
            {
                DefaultStoreTimeZoneId = string.Empty,
                AllowCustomersToSetTimeZone = false
            });

            await settingService.SaveSettingAsync(new BlogSettings
            {
                Enabled = true,
                PostsPageSize = 10,
                AllowNotRegisteredUsersToLeaveComments = true,
                NotifyAboutNewBlogComments = false,
                NumberOfTags = 15,
                ShowHeaderRssUrl = false,
                BlogCommentsMustBeApproved = false,
                ShowBlogCommentsPerStore = false
            });
            await settingService.SaveSettingAsync(new NewsSettings
            {
                Enabled = true,
                AllowNotRegisteredUsersToLeaveComments = true,
                NotifyAboutNewNewsComments = false,
                ShowNewsOnMainPage = true,
                MainPageNewsCount = 3,
                NewsArchivePageSize = 10,
                ShowHeaderRssUrl = false,
                NewsCommentsMustBeApproved = false,
                ShowNewsCommentsPerStore = false
            });

            await settingService.SaveSettingAsync(new ForumSettings
            {
                ForumsEnabled = false,
                RelativeDateTimeFormattingEnabled = true,
                AllowCustomersToDeletePosts = false,
                AllowCustomersToEditPosts = false,
                AllowCustomersToManageSubscriptions = false,
                AllowGuestsToCreatePosts = false,
                AllowGuestsToCreateTopics = false,
                AllowPostVoting = true,
                MaxVotesPerDay = 30,
                TopicSubjectMaxLength = 450,
                PostMaxLength = 4000,
                StrippedTopicMaxLength = 45,
                TopicsPageSize = 10,
                PostsPageSize = 10,
                SearchResultsPageSize = 10,
                ActiveDiscussionsPageSize = 50,
                LatestCustomerPostsPageSize = 10,
                ShowCustomersPostCount = true,
                ForumEditor = EditorType.BBCodeEditor,
                SignaturesEnabled = true,
                AllowPrivateMessages = false,
                ShowAlertForPM = false,
                PrivateMessagesPageSize = 10,
                ForumSubscriptionsPageSize = 10,
                NotifyAboutPrivateMessages = false,
                PMSubjectMaxLength = 450,
                PMTextMaxLength = 4000,
                HomepageActiveDiscussionsTopicCount = 5,
                ActiveDiscussionsFeedEnabled = false,
                ActiveDiscussionsFeedCount = 25,
                ForumFeedsEnabled = false,
                ForumFeedCount = 10,
                ForumSearchTermMinimumLength = 3
            });

            await settingService.SaveSettingAsync(new VendorSettings
            {
                DefaultVendorPageSizeOptions = "6, 3, 9",
                VendorsBlockItemsToDisplay = 0,
                ShowVendorOnProductDetailsPage = true,
                AllowCustomersToContactVendors = true,
                AllowCustomersToApplyForVendorAccount = true,
                TermsOfServiceEnabled = false,
                AllowVendorsToEditInfo = false,
                NotifyStoreOwnerAboutVendorInformationChange = true,
                MaximumProductNumber = 3000,
                AllowVendorsToImportProducts = true
            });

            var eaGeneral = _emailAccountRepository.Table.FirstOrDefault();
            if (eaGeneral == null)
                throw new Exception("Default email account cannot be loaded");
            await settingService.SaveSettingAsync(new EmailAccountSettings
            {
                DefaultEmailAccountId = eaGeneral.Id
            });

            await settingService.SaveSettingAsync(new WidgetSettings
            {
                ActiveWidgetSystemNames = new List<string> {}
            });

            await settingService.SaveSettingAsync(new DisplayDefaultMenuItemSettings
            {
                DisplayHomepageMenuItem = true,
                DisplayNewProductsMenuItem = false,
                DisplayProductSearchMenuItem = true,
                DisplayCustomerInfoMenuItem = true,
                DisplayBlogMenuItem = false,
                DisplayForumsMenuItem = false,
                DisplayContactUsMenuItem = true
            });

            await settingService.SaveSettingAsync(new DisplayDefaultFooterItemSettings
            {
                DisplaySitemapFooterItem = false,
                DisplayContactUsFooterItem = true,
                DisplayProductSearchFooterItem = false,
                DisplayNewsFooterItem = false,
                DisplayBlogFooterItem = false,
                DisplayForumsFooterItem = false,
                DisplayRecentlyViewedProductsFooterItem = true,
                DisplayCompareProductsFooterItem = true,
                DisplayNewProductsFooterItem = true,
                DisplayCustomerInfoFooterItem = true,
                DisplayCustomerOrdersFooterItem = true,
                DisplayCustomerAddressesFooterItem = false,
                DisplayShoppingCartFooterItem = false,
                DisplayWishlistFooterItem = true,
                DisplayApplyVendorAccountFooterItem = false
            });

            await settingService.SaveSettingAsync(new CaptchaSettings
            {
                ReCaptchaApiUrl = "https://www.google.com/recaptcha/",
                ReCaptchaDefaultLanguage = string.Empty,
                ReCaptchaPrivateKey = string.Empty,
                ReCaptchaPublicKey = string.Empty,
                ReCaptchaRequestTimeout = 20,
                ReCaptchaTheme = string.Empty,
                AutomaticallyChooseLanguage = true,
                Enabled = false,
                CaptchaType = CaptchaType.CheckBoxReCaptchaV2,
                ReCaptchaV3ScoreThreshold = 0.5M,
                ShowOnApplyVendorPage = false,
                ShowOnBlogCommentPage = false,
                ShowOnContactUsPage = false,
                ShowOnEmailProductToFriendPage = false,
                ShowOnEmailWishlistToFriendPage = false,
                ShowOnForgotPasswordPage = false,
                ShowOnForum = false,
                ShowOnLoginPage = false,
                ShowOnNewsCommentPage = false,
                ShowOnProductReviewPage = false,
                ShowOnRegistrationPage = false,
            });

            await settingService.SaveSettingAsync(new MessagesSettings
            {
                UsePopupNotifications = false
            });

            await settingService.SaveSettingAsync(new ProxySettings
            {
                Enabled = false,
                Address = string.Empty,
                Port = string.Empty,
                Username = string.Empty,
                Password = string.Empty,
                BypassOnLocal = true,
                PreAuthenticate = true
            });

            await settingService.SaveSettingAsync(new CookieSettings
            {
                CompareProductsCookieExpires = 24 * 10,
                RecentlyViewedProductsCookieExpires = 24 * 10,
                CustomerCookieExpires = 24 * 365
            });
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallActivityLogTypesAsync()
        {
            var activityLogTypes = new List<ActivityLogType>
            {
                //admin area activities
                new ActivityLogType
                {
                    SystemKeyword = "AddNewAddressAttribute",
                    Enabled = true,
                    Name = "Add a new address attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewAddressAttributeValue",
                    Enabled = true,
                    Name = "Add a new address attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewAffiliate",
                    Enabled = true,
                    Name = "Add a new affiliate"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewBlogPost",
                    Enabled = true,
                    Name = "Add a new blog post"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCampaign",
                    Enabled = true,
                    Name = "Add a new campaign"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCategory",
                    Enabled = true,
                    Name = "Add a new category"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCheckoutAttribute",
                    Enabled = true,
                    Name = "Add a new checkout attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCountry",
                    Enabled = true,
                    Name = "Add a new country"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCurrency",
                    Enabled = true,
                    Name = "Add a new currency"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCustomer",
                    Enabled = true,
                    Name = "Add a new customer"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCustomerAttribute",
                    Enabled = true,
                    Name = "Add a new customer attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCustomerAttributeValue",
                    Enabled = true,
                    Name = "Add a new customer attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewCustomerRole",
                    Enabled = true,
                    Name = "Add a new customer role"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewDiscount",
                    Enabled = true,
                    Name = "Add a new discount"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewEmailAccount",
                    Enabled = true,
                    Name = "Add a new email account"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewGiftCard",
                    Enabled = true,
                    Name = "Add a new gift card"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewLanguage",
                    Enabled = true,
                    Name = "Add a new language"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewManufacturer",
                    Enabled = true,
                    Name = "Add a new manufacturer"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewMeasureDimension",
                    Enabled = true,
                    Name = "Add a new measure dimension"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewMeasureWeight",
                    Enabled = true,
                    Name = "Add a new measure weight"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewNews",
                    Enabled = true,
                    Name = "Add a new news"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewProduct",
                    Enabled = true,
                    Name = "Add a new product"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewProductAttribute",
                    Enabled = true,
                    Name = "Add a new product attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewSetting",
                    Enabled = true,
                    Name = "Add a new setting"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewSpecAttribute",
                    Enabled = true,
                    Name = "Add a new specification attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewSpecAttributeGroup",
                    Enabled = true,
                    Name = "Add a new specification attribute group"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewStateProvince",
                    Enabled = true,
                    Name = "Add a new state or province"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewStore",
                    Enabled = true,
                    Name = "Add a new store"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewTopic",
                    Enabled = true,
                    Name = "Add a new topic"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewReviewType",
                    Enabled = true,
                    Name = "Add a new review type"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewVendor",
                    Enabled = true,
                    Name = "Add a new vendor"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewVendorAttribute",
                    Enabled = true,
                    Name = "Add a new vendor attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewVendorAttributeValue",
                    Enabled = true,
                    Name = "Add a new vendor attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewWarehouse",
                    Enabled = true,
                    Name = "Add a new warehouse"
                },
                new ActivityLogType
                {
                    SystemKeyword = "AddNewWidget",
                    Enabled = true,
                    Name = "Add a new widget"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteActivityLog",
                    Enabled = true,
                    Name = "Delete activity log"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteAddressAttribute",
                    Enabled = true,
                    Name = "Delete an address attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteAddressAttributeValue",
                    Enabled = true,
                    Name = "Delete an address attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteAffiliate",
                    Enabled = true,
                    Name = "Delete an affiliate"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteBlogPost",
                    Enabled = true,
                    Name = "Delete a blog post"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteBlogPostComment",
                    Enabled = true,
                    Name = "Delete a blog post comment"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCampaign",
                    Enabled = true,
                    Name = "Delete a campaign"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCategory",
                    Enabled = true,
                    Name = "Delete category"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCheckoutAttribute",
                    Enabled = true,
                    Name = "Delete a checkout attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCountry",
                    Enabled = true,
                    Name = "Delete a country"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCurrency",
                    Enabled = true,
                    Name = "Delete a currency"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCustomer",
                    Enabled = true,
                    Name = "Delete a customer"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCustomerAttribute",
                    Enabled = true,
                    Name = "Delete a customer attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCustomerAttributeValue",
                    Enabled = true,
                    Name = "Delete a customer attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteCustomerRole",
                    Enabled = true,
                    Name = "Delete a customer role"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteDiscount",
                    Enabled = true,
                    Name = "Delete a discount"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteEmailAccount",
                    Enabled = true,
                    Name = "Delete an email account"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteGiftCard",
                    Enabled = true,
                    Name = "Delete a gift card"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteLanguage",
                    Enabled = true,
                    Name = "Delete a language"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteManufacturer",
                    Enabled = true,
                    Name = "Delete a manufacturer"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteMeasureDimension",
                    Enabled = true,
                    Name = "Delete a measure dimension"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteMeasureWeight",
                    Enabled = true,
                    Name = "Delete a measure weight"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteMessageTemplate",
                    Enabled = true,
                    Name = "Delete a message template"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteNews",
                    Enabled = true,
                    Name = "Delete a news"
                },
                 new ActivityLogType
                {
                    SystemKeyword = "DeleteNewsComment",
                    Enabled = true,
                    Name = "Delete a news comment"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteOrder",
                    Enabled = true,
                    Name = "Delete an order"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeletePlugin",
                    Enabled = true,
                    Name = "Delete a plugin"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteProduct",
                    Enabled = true,
                    Name = "Delete a product"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteProductAttribute",
                    Enabled = true,
                    Name = "Delete a product attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteProductReview",
                    Enabled = true,
                    Name = "Delete a product review"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteReturnRequest",
                    Enabled = true,
                    Name = "Delete a return request"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteReviewType",
                    Enabled = true,
                    Name = "Delete a review type"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteSetting",
                    Enabled = true,
                    Name = "Delete a setting"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteSpecAttribute",
                    Enabled = true,
                    Name = "Delete a specification attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteSpecAttributeGroup",
                    Enabled = true,
                    Name = "Delete a specification attribute group"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteStateProvince",
                    Enabled = true,
                    Name = "Delete a state or province"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteStore",
                    Enabled = true,
                    Name = "Delete a store"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteSystemLog",
                    Enabled = true,
                    Name = "Delete system log"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteTopic",
                    Enabled = true,
                    Name = "Delete a topic"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteVendor",
                    Enabled = true,
                    Name = "Delete a vendor"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteVendorAttribute",
                    Enabled = true,
                    Name = "Delete a vendor attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteVendorAttributeValue",
                    Enabled = true,
                    Name = "Delete a vendor attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteWarehouse",
                    Enabled = true,
                    Name = "Delete a warehouse"
                },
                new ActivityLogType
                {
                    SystemKeyword = "DeleteWidget",
                    Enabled = true,
                    Name = "Delete a widget"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditActivityLogTypes",
                    Enabled = true,
                    Name = "Edit activity log types"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditAddressAttribute",
                    Enabled = true,
                    Name = "Edit an address attribute"
                },
                 new ActivityLogType
                {
                    SystemKeyword = "EditAddressAttributeValue",
                    Enabled = true,
                    Name = "Edit an address attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditAffiliate",
                    Enabled = true,
                    Name = "Edit an affiliate"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditBlogPost",
                    Enabled = true,
                    Name = "Edit a blog post"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCampaign",
                    Enabled = true,
                    Name = "Edit a campaign"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCategory",
                    Enabled = true,
                    Name = "Edit category"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCheckoutAttribute",
                    Enabled = true,
                    Name = "Edit a checkout attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCountry",
                    Enabled = true,
                    Name = "Edit a country"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCurrency",
                    Enabled = true,
                    Name = "Edit a currency"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCustomer",
                    Enabled = true,
                    Name = "Edit a customer"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCustomerAttribute",
                    Enabled = true,
                    Name = "Edit a customer attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCustomerAttributeValue",
                    Enabled = true,
                    Name = "Edit a customer attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditCustomerRole",
                    Enabled = true,
                    Name = "Edit a customer role"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditDiscount",
                    Enabled = true,
                    Name = "Edit a discount"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditEmailAccount",
                    Enabled = true,
                    Name = "Edit an email account"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditGiftCard",
                    Enabled = true,
                    Name = "Edit a gift card"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditLanguage",
                    Enabled = true,
                    Name = "Edit a language"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditManufacturer",
                    Enabled = true,
                    Name = "Edit a manufacturer"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditMeasureDimension",
                    Enabled = true,
                    Name = "Edit a measure dimension"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditMeasureWeight",
                    Enabled = true,
                    Name = "Edit a measure weight"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditMessageTemplate",
                    Enabled = true,
                    Name = "Edit a message template"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditNews",
                    Enabled = true,
                    Name = "Edit a news"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditOrder",
                    Enabled = true,
                    Name = "Edit an order"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditPlugin",
                    Enabled = true,
                    Name = "Edit a plugin"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditProduct",
                    Enabled = true,
                    Name = "Edit a product"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditProductAttribute",
                    Enabled = true,
                    Name = "Edit a product attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditProductReview",
                    Enabled = true,
                    Name = "Edit a product review"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditPromotionProviders",
                    Enabled = true,
                    Name = "Edit promotion providers"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditReturnRequest",
                    Enabled = true,
                    Name = "Edit a return request"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditReviewType",
                    Enabled = true,
                    Name = "Edit a review type"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditSettings",
                    Enabled = true,
                    Name = "Edit setting(s)"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditStateProvince",
                    Enabled = true,
                    Name = "Edit a state or province"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditStore",
                    Enabled = true,
                    Name = "Edit a store"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditTask",
                    Enabled = true,
                    Name = "Edit a task"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditSpecAttribute",
                    Enabled = true,
                    Name = "Edit a specification attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditSpecAttributeGroup",
                    Enabled = true,
                    Name = "Edit a specification attribute group"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditVendor",
                    Enabled = true,
                    Name = "Edit a vendor"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditVendorAttribute",
                    Enabled = true,
                    Name = "Edit a vendor attribute"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditVendorAttributeValue",
                    Enabled = true,
                    Name = "Edit a vendor attribute value"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditWarehouse",
                    Enabled = true,
                    Name = "Edit a warehouse"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditTopic",
                    Enabled = true,
                    Name = "Edit a topic"
                },
                new ActivityLogType
                {
                    SystemKeyword = "EditWidget",
                    Enabled = true,
                    Name = "Edit a widget"
                },
                new ActivityLogType
                {
                    SystemKeyword = "Impersonation.Started",
                    Enabled = true,
                    Name = "Customer impersonation session. Started"
                },
                new ActivityLogType
                {
                    SystemKeyword = "Impersonation.Finished",
                    Enabled = true,
                    Name = "Customer impersonation session. Finished"
                },
                new ActivityLogType
                {
                    SystemKeyword = "ImportCategories",
                    Enabled = true,
                    Name = "Categories were imported"
                },
                new ActivityLogType
                {
                    SystemKeyword = "ImportManufacturers",
                    Enabled = true,
                    Name = "Manufacturers were imported"
                },
                new ActivityLogType
                {
                    SystemKeyword = "ImportProducts",
                    Enabled = true,
                    Name = "Products were imported"
                },
                new ActivityLogType
                {
                    SystemKeyword = "ImportStates",
                    Enabled = true,
                    Name = "States were imported"
                },
                new ActivityLogType
                {
                    SystemKeyword = "InstallNewPlugin",
                    Enabled = true,
                    Name = "Install a new plugin"
                },
                new ActivityLogType
                {
                    SystemKeyword = "UninstallPlugin",
                    Enabled = true,
                    Name = "Uninstall a plugin"
                },
                //public store activities
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.ViewCategory",
                    Enabled = false,
                    Name = "Public store. View a category"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.ViewManufacturer",
                    Enabled = false,
                    Name = "Public store. View a manufacturer"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.ViewProduct",
                    Enabled = false,
                    Name = "Public store. View a product"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.PlaceOrder",
                    Enabled = false,
                    Name = "Public store. Place an order"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.SendPM",
                    Enabled = false,
                    Name = "Public store. Send PM"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.ContactUs",
                    Enabled = false,
                    Name = "Public store. Use contact us form"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddToCompareList",
                    Enabled = false,
                    Name = "Public store. Add to compare list"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddToShoppingCart",
                    Enabled = false,
                    Name = "Public store. Add to shopping cart"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddToWishlist",
                    Enabled = false,
                    Name = "Public store. Add to wishlist"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.Login",
                    Enabled = false,
                    Name = "Public store. Login"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.Logout",
                    Enabled = false,
                    Name = "Public store. Logout"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddProductReview",
                    Enabled = false,
                    Name = "Public store. Add product review"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddNewsComment",
                    Enabled = false,
                    Name = "Public store. Add news comment"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddBlogComment",
                    Enabled = false,
                    Name = "Public store. Add blog comment"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddForumTopic",
                    Enabled = false,
                    Name = "Public store. Add forum topic"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.EditForumTopic",
                    Enabled = false,
                    Name = "Public store. Edit forum topic"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.DeleteForumTopic",
                    Enabled = false,
                    Name = "Public store. Delete forum topic"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.AddForumPost",
                    Enabled = false,
                    Name = "Public store. Add forum post"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.EditForumPost",
                    Enabled = false,
                    Name = "Public store. Edit forum post"
                },
                new ActivityLogType
                {
                    SystemKeyword = "PublicStore.DeleteForumPost",
                    Enabled = false,
                    Name = "Public store. Delete forum post"
                },
                new ActivityLogType
                {
                    SystemKeyword = "UploadNewPlugin",
                    Enabled = true,
                    Name = "Upload a plugin"
                },
                new ActivityLogType
                {
                    SystemKeyword = "UploadNewTheme",
                    Enabled = true,
                    Name = "Upload a theme"
                },
                new ActivityLogType
                {
                    SystemKeyword = "UploadIcons",
                    Enabled = true,
                    Name = "Upload a favicon and app icons"
                }
            };

            await InsertInstallationDataAsync(activityLogTypes);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallProductTemplatesAsync()
        {
            var productTemplates = new List<ProductTemplate>
            {
                new ProductTemplate
                {
                    Name = "Simple product",
                    ViewPath = "ProductTemplate.Simple",
                    DisplayOrder = 10,
                    IgnoredProductTypes = ((int)ProductType.GroupedProduct).ToString()
                },
                new ProductTemplate
                {
                    Name = "Grouped product (with variants)",
                    ViewPath = "ProductTemplate.Grouped",
                    DisplayOrder = 100,
                    IgnoredProductTypes = ((int)ProductType.SimpleProduct).ToString()
                }
            };

            await InsertInstallationDataAsync(productTemplates);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallCategoryTemplatesAsync()
        {
            var categoryTemplates = new List<CategoryTemplate>
            {
                new CategoryTemplate
                {
                    Name = "Products in Grid or Lines",
                    ViewPath = "CategoryTemplate.ProductsInGridOrLines",
                    DisplayOrder = 1
                }
            };

            await InsertInstallationDataAsync(categoryTemplates);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallManufacturerTemplatesAsync()
        {
            var manufacturerTemplates = new List<ManufacturerTemplate>
            {
                new ManufacturerTemplate
                {
                    Name = "Products in Grid or Lines",
                    ViewPath = "ManufacturerTemplate.ProductsInGridOrLines",
                    DisplayOrder = 1
                }
            };

            await InsertInstallationDataAsync(manufacturerTemplates);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallTopicTemplatesAsync()
        {
            var topicTemplates = new List<TopicTemplate>
            {
                new TopicTemplate
                {
                    Name = "Default template",
                    ViewPath = "TopicDetails",
                    DisplayOrder = 1
                }
            };

            await InsertInstallationDataAsync(topicTemplates);
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task InstallScheduleTasksAsync()
        {
            var lastEnabledUtc = DateTime.UtcNow;
            var tasks = new List<ScheduleTask>
            {
                new ScheduleTask
                {
                    Name = "Send emails",
                    Seconds = 60,
                    Type = "Nop.Services.Messages.QueuedMessagesSendTask, Nop.Services",
                    Enabled = true,
                    LastEnabledUtc = lastEnabledUtc,
                    StopOnError = false
                },
                new ScheduleTask
                {
                    Name = "Keep alive",
                    Seconds = 300,
                    Type = "Nop.Services.Common.KeepAliveTask, Nop.Services",
                    Enabled = true,
                    LastEnabledUtc = lastEnabledUtc,
                    StopOnError = false
                },
                new ScheduleTask
                {
                    Name = "Delete guests",
                    Seconds = 600,
                    Type = "Nop.Services.Customers.DeleteGuestsTask, Nop.Services",
                    Enabled = true,
                    LastEnabledUtc = lastEnabledUtc,
                    StopOnError = false
                },
                new ScheduleTask
                {
                    Name = "Clear cache",
                    Seconds = 600,
                    Type = "Nop.Services.Caching.ClearCacheTask, Nop.Services",
                    Enabled = false,
                    StopOnError = false
                },
                new ScheduleTask
                {
                    Name = "Clear log",
                    //60 minutes
                    Seconds = 3600,
                    Type = "Nop.Services.Logging.ClearLogTask, Nop.Services",
                    Enabled = false,
                    StopOnError = false
                },
                new ScheduleTask
                {
                    Name = "Update currency exchange rates",
                    //60 minutes
                    Seconds = 3600,
                    Type = "Nop.Services.Directory.UpdateExchangeRateTask, Nop.Services",
                    Enabled = true,
                    LastEnabledUtc = lastEnabledUtc,
                    StopOnError = false
                }
            };

            await InsertInstallationDataAsync(tasks);
        }
        
        #endregion

        #region Methods

        /// <summary>
        /// Install required data
        /// </summary>
        /// <param name="defaultUserEmail">Default user email</param>
        /// <param name="defaultUserPassword">Default user password</param>
        /// <param name="languagePackInfo">Language pack info</param>
        /// <param name="regionInfo">RegionInfo</param>
        /// <param name="cultureInfo">CultureInfo</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual async Task InstallRequiredDataAsync(string defaultUserEmail, string defaultUserPassword,
            (string languagePackDownloadLink, int languagePackProgress) languagePackInfo, RegionInfo regionInfo, CultureInfo cultureInfo)
        {
            await InstallStoresAsync();
            await InstallMeasuresAsync(regionInfo);
            await InstallTaxCategoriesAsync();
            await InstallLanguagesAsync(languagePackInfo, cultureInfo, regionInfo);
            await InstallCurrenciesAsync(cultureInfo, regionInfo);
            await InstallCountriesAndStatesAsync();
            await InstallEmailAccountsAsync();
            await InstallMessageTemplatesAsync(); // needs translation
            await InstallTopicTemplatesAsync();
            await InstallSettingsAsync(regionInfo);
            await InstallCustomersAndUsersAsync(defaultUserEmail, defaultUserPassword);
            await InstallTopicsAsync();
            await InstallActivityLogTypesAsync();
            await InstallProductTemplatesAsync();
            await InstallCategoryTemplatesAsync();
            await InstallManufacturerTemplatesAsync();
            await InstallScheduleTasksAsync();
        }
        
        #endregion
    }
}