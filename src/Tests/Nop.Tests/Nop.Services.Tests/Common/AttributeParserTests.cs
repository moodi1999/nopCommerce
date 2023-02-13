using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
using Nop.Core.Domain.Attributes;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Vendors;
using Nop.Services.Attributes;
using NUnit.Framework;


namespace Nop.Tests.Nop.Services.Tests.Common
{
    [TestFixture]
    public class AttributeParserTests : BaseNopTest
    {
        private string _attributesXml;
        private XmlDocument _xmlDoc;
        protected dynamic _parser;
        private IAttributeService<CustomerAttribute, CustomerAttributeValue> _customerAttributeService;
        private IAttributeService<VendorAttribute, VendorAttributeValue> _vendorAttributeService;
        private IAttributeService<AddressAttribute, AddressAttributeValue> _addressAttributeService;
        private IAttributeService<CheckoutAttribute, CheckoutAttributeValue> _checkoutAttributeService;

        protected void PrepareTestData(Type attributeType)
        {
            _attributesXml = $"<Attributes><{attributeType.Name} ID=\"1\"><{attributeType.Name}Value><Value>1</Value></{attributeType.Name}Value></{attributeType.Name}><{attributeType.Name} ID=\"2\"><{attributeType.Name}Value><Value>2</Value></{attributeType.Name}Value></{attributeType.Name}></Attributes>";

            if (attributeType == typeof(CustomerAttribute)) 
                _parser = GetService<IAttributeParser<CustomerAttribute, CustomerAttributeValue>>();

            if (attributeType == typeof(VendorAttribute))
                _parser = GetService<IAttributeParser<VendorAttribute, VendorAttributeValue>>();

            if (attributeType == typeof(AddressAttribute))
                _parser = GetService<IAttributeParser<AddressAttribute, AddressAttributeValue>>();

            if (attributeType == typeof(CheckoutAttribute))
                _parser = GetService<IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>>();
        }
        

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _xmlDoc = new XmlDocument();

            _customerAttributeService = GetService<IAttributeService<CustomerAttribute, CustomerAttributeValue>>();
            _vendorAttributeService = GetService<IAttributeService<VendorAttribute, VendorAttributeValue>>();
            _addressAttributeService = GetService<IAttributeService<AddressAttribute, AddressAttributeValue>>();
            _checkoutAttributeService = GetService<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>>();

            for (var i=0; i < 2; i++)
            {
                BaseAttribute attribute = new CustomerAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test"
                };
                await _customerAttributeService.InsertAttributeAsync((CustomerAttribute)attribute);
                BaseAttributeValue attributeValue = new CustomerAttributeValue { AttributeId = attribute.Id, Name = "test"};
                await _customerAttributeService.InsertAttributeValueAsync((CustomerAttributeValue)attributeValue);
                attributeValue = new CustomerAttributeValue { AttributeId = attribute.Id, Name = "test" };
                await _customerAttributeService.InsertAttributeValueAsync((CustomerAttributeValue)attributeValue);
                attribute = new CustomerAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test",
                    IsRequired = true
                };
                await _customerAttributeService.InsertAttributeAsync((CustomerAttribute)attribute);

                attribute = new VendorAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test"
                };
                await _vendorAttributeService.InsertAttributeAsync((VendorAttribute)attribute);
                attributeValue = new VendorAttributeValue { AttributeId = attribute.Id, Name = "test" };
                await _vendorAttributeService.InsertAttributeValueAsync((VendorAttributeValue)attributeValue);
                attributeValue = new VendorAttributeValue { AttributeId = attribute.Id, Name = "test" };
                await _vendorAttributeService.InsertAttributeValueAsync((VendorAttributeValue)attributeValue);
                attribute = new VendorAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test",
                    IsRequired = true
                };
                await _vendorAttributeService.InsertAttributeAsync((VendorAttribute)attribute);

                attribute = new AddressAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test"
                };
                await _addressAttributeService.InsertAttributeAsync((AddressAttribute)attribute);
                attributeValue = new AddressAttributeValue { AttributeId = attribute.Id, Name = "test" };
                await _addressAttributeService.InsertAttributeValueAsync((AddressAttributeValue)attributeValue);
                attributeValue = new AddressAttributeValue { AttributeId = attribute.Id, Name = "test" };
                await _addressAttributeService.InsertAttributeValueAsync((AddressAttributeValue)attributeValue);
                attribute = new AddressAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test",
                    IsRequired = true
                };
                await _addressAttributeService.InsertAttributeAsync((AddressAttribute)attribute);

                attribute = new CheckoutAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test"
                };
                await _checkoutAttributeService.InsertAttributeAsync((CheckoutAttribute)attribute);
                attributeValue = new CheckoutAttributeValue { AttributeId = attribute.Id, Name = "test" };
                await _checkoutAttributeService.InsertAttributeValueAsync((CheckoutAttributeValue)attributeValue);
                attributeValue = new CheckoutAttributeValue { AttributeId = attribute.Id, Name = "test" };
                await _checkoutAttributeService.InsertAttributeValueAsync((CheckoutAttributeValue)attributeValue);
                attribute = new CheckoutAttribute
                {
                    AttributeControlType = AttributeControlType.DropdownList,
                    Name = "test",
                    IsRequired = true
                };
                await _checkoutAttributeService.InsertAttributeAsync((CheckoutAttribute)attribute);
            }
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            for (var i = 1; i < 5; i++)
            {
                await _customerAttributeService.DeleteAttributeValueAsync(new CustomerAttributeValue { Id = i });
                await _vendorAttributeService.DeleteAttributeValueAsync(new VendorAttributeValue { Id = i });
                await _addressAttributeService.DeleteAttributeValueAsync(new AddressAttributeValue { Id = i });
                await _checkoutAttributeService.DeleteAttributeValueAsync(new CheckoutAttributeValue { Id = i });
            }

            for (var i = 1; i < 5; i++)
            {
                await _customerAttributeService.DeleteAttributeAsync(new CustomerAttribute { Id = i });
                await _vendorAttributeService.DeleteAttributeAsync(new VendorAttribute { Id = i });
                await _addressAttributeService.DeleteAttributeAsync(new AddressAttribute { Id = i });
                await _checkoutAttributeService.DeleteAttributeAsync(new CheckoutAttribute { Id = i });
            }
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]

        public void CanRemoveAttribute(Type attributeType)
        {
            PrepareTestData(attributeType);

            var xml = _parser.RemoveAttribute(_attributesXml, 1) as string;
            _xmlDoc.LoadXml(xml);
            var childNodes = _xmlDoc.SelectNodes($@"//Attributes/{attributeType.Name}");

            childNodes?.Count.Should().Be(1);
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]

        public async Task CanAddAttribute(Type attributeType)
        {
            PrepareTestData(attributeType);
            
            var attribute = (await _parser.ParseAttributesAsync(_attributesXml))[1];
            (attribute as BaseAttribute).Should().NotBeNull();

            var values = await _parser.ParseAttributeValuesAsync(_attributesXml);
            ((int)values.Count).Should().Be(2);

            var xml = _parser.AddAttribute(_attributesXml, attribute, "3") as string;

            values = await _parser.ParseAttributeValuesAsync(xml);
            ((int)values.Count).Should().Be(3);
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]
        public void RemoveAttributeShouldReturnEmptyStringWhenAllChildrenIsRemoved(Type attributeType)
        {
            PrepareTestData(attributeType);
            var xml = _parser.RemoveAttribute(_attributesXml, 1) as string;
            xml = _parser.RemoveAttribute(xml, 2);

            xml.Should().BeEmpty();
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]
        public void RemoveAttributeShouldNotChangeXmlIfAttributeValueIdNotExists(Type attributeType)
        {
            PrepareTestData(attributeType);
            var xml = _parser.RemoveAttribute(_attributesXml, 3) as string;

            xml?.Equals(_attributesXml).Should().BeTrue();
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]
        public void CanParseAttributeIds(Type attributeType)
        {
            PrepareTestData(attributeType);
            var ids = _parser.ParseAttributeIds(_attributesXml) as IList<int>;

            var existsId = new List<int> { 1, 2 };

            ids?.Count.Should().Be(2);
            ids?.All(existsId.Contains).Should().BeTrue();
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]
        public void CanParseAttributeIdsShouldReturnEmptyListIfXmlIsEmpty(Type attributeType)
        {
            PrepareTestData(attributeType);
            var ids = _parser.ParseAttributeIds(string.Empty) as IList<int>;

            ids?.Count.Should().Be(0);
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]
        public void CanParseAttributeIdsShouldReturnEmptyListIfXmlNotContainsRightAttributes(Type attributeType)
        {
            PrepareTestData(attributeType);
            var ids = _parser.ParseAttributeIds("<Attributes><Item ID=\"1\">Test 1</Item><Item ID=\"2\">Test 2</Item></Attributes>") as IList<int>;

            ids?.Count.Should().Be(0);

            ids = _parser.ParseAttributeIds("<Items><Attribute ID=\"1\">Test 1</Attribute><Attribute ID=\"2\">Test 2</Attribute></Items>") as IList<int>;

            ids?.Count.Should().Be(0);
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]
        public void CanParseAttributeValues(Type attributeType)
        {
            PrepareTestData(attributeType);

            var values = _parser.ParseAttributeValuesAsync(_attributesXml).Result?.Count as int?;
            values.Should().Be(2);
        }

        [Test]
        [TestCase(typeof(CustomerAttribute))]
        [TestCase(typeof(VendorAttribute))]
        [TestCase(typeof(AddressAttribute))]
        [TestCase(typeof(CheckoutAttribute))]

        public async Task GetAttributeWarnings(Type attributeType)
        {
            PrepareTestData(attributeType);

            var attribute = (await _parser.ParseAttributesAsync(_attributesXml))[1];
            (attribute as BaseAttribute).Should().NotBeNull();

            var values = await _parser.GetAttributeWarningsAsync(_attributesXml);
            ((int)values.Count).Should().BeGreaterOrEqualTo(1);
        }
    }
}
